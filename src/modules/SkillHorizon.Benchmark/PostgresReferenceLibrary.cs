using System.Text.Json;
using Npgsql;
using SkillHorizon.Taxonomy;

namespace SkillHorizon.Benchmark;

public sealed class PostgresReferenceLibrary : IReferenceLibrary
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;

    public PostgresReferenceLibrary(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<int> ApplyPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var pending = new List<(Guid Id, string Type, string Payload)>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT outbox.outbox_id, outbox.message_type, outbox.payload
            FROM messaging.outbox AS outbox
            WHERE outbox.message_type IN ('taxonomy.published', 'benchmark.published')
              AND NOT EXISTS (
                  SELECT 1 FROM reference.applied_message AS applied WHERE applied.outbox_id = outbox.outbox_id)
            ORDER BY outbox.occurred_at
            FOR UPDATE OF outbox
            """,
            connection,
            transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                pending.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        foreach (var (id, type, payload) in pending)
        {
            if (type == TaxonomyModule.PublishedMessage)
                await CopyTaxonomy(connection, transaction, payload, cancellationToken).ConfigureAwait(false);
            else if (type == BenchmarkModule.PublishedMessage)
                await CopyBenchmark(connection, transaction, payload, cancellationToken).ConfigureAwait(false);

            await using var applied = new NpgsqlCommand(
                "INSERT INTO reference.applied_message (outbox_id, applied_at) VALUES ($1, now())",
                connection,
                transaction);
            applied.Parameters.AddWithValue(id);
            await applied.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return pending.Count;
    }

    public async Task ApplyPublicationAsync(Guid messageId, string messageType, string payload, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var existing = new NpgsqlCommand(
            "SELECT 1 FROM reference.applied_message WHERE outbox_id = $1",
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue(messageId);
            if (await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        if (messageType == TaxonomyModule.PublishedMessage)
            await CopyTaxonomy(connection, transaction, payload, cancellationToken).ConfigureAwait(false);
        else if (messageType == BenchmarkModule.PublishedMessage)
            await CopyBenchmark(connection, transaction, payload, cancellationToken).ConfigureAwait(false);

        await using (var applied = new NpgsqlCommand(
            "INSERT INTO reference.applied_message (outbox_id, applied_at) VALUES ($1, now())",
            connection,
            transaction))
        {
            applied.Parameters.AddWithValue(messageId);
            await applied.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<BenchmarkVersionView>> ListAsync(CancellationToken cancellationToken) =>
        ReadHistory("TRUE", null, cancellationToken);

    public async Task<TaxonomyVersionView?> ReadPublishedTaxonomyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Guid versionId;
        int versionNumber;
        await using (var header = new NpgsqlCommand(
            """
            SELECT taxonomy_version_id, version_number
            FROM reference.taxonomy_version
            WHERE status = 'published'
            ORDER BY version_number DESC
            LIMIT 1
            """,
            connection))
        await using (var reader = await header.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            versionId = reader.GetGuid(0);
            versionNumber = reader.GetInt32(1);
        }

        var skills = new List<TaxonomySkillView>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT skill_id, skill_code, skill_name, competency_code, competency_name, practice_code, practice_name
            FROM reference.taxonomy_skill
            WHERE taxonomy_version_id = $1
            ORDER BY practice_name, competency_name, skill_name
            """,
            connection))
        {
            command.Parameters.AddWithValue(versionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                skills.Add(new TaxonomySkillView(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        var proficiencies = new List<ProficiencyView>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT code, display_name, rank
            FROM reference.proficiency
            WHERE taxonomy_version_id = $1
            ORDER BY rank
            """,
            connection))
        {
            command.Parameters.AddWithValue(versionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                proficiencies.Add(new ProficiencyView(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return new TaxonomyVersionView(versionId, versionNumber, "published", skills, proficiencies);
    }

    public Task<IReadOnlyList<BenchmarkVersionView>> HistoryAsync(Guid jobProfileId, CancellationToken cancellationToken) =>
        ReadHistory("job_profile_id = $1", jobProfileId, cancellationToken);

    private async Task<IReadOnlyList<BenchmarkVersionView>> ReadHistory(
        string where,
        Guid? jobProfileId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var versions = new List<BenchmarkVersionView>();
        await using (var command = new NpgsqlCommand(
            $"""
            SELECT benchmark_version_id, job_profile_id, profile_code, profile_name,
                   framework_code, framework_name, level_code, level_name, level_rank,
                   taxonomy_version_id, version_number, status, supersedes_version_id, superseded_by
            FROM reference.benchmark_version
            WHERE {where}
            ORDER BY profile_name, version_number
            """,
            connection))
        {
            if (jobProfileId is Guid profileId)
                command.Parameters.AddWithValue(profileId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                versions.Add(new BenchmarkVersionView(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetInt32(8),
                    reader.GetGuid(9),
                    reader.GetInt32(10),
                    reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetGuid(12),
                    reader.IsDBNull(13) ? null : reader.GetGuid(13),
                    []));
            }
        }

        if (versions.Count == 0)
            return versions;

        var targets = new Dictionary<Guid, List<TargetView>>();
        await using (var command = new NpgsqlCommand(
            $"""
            SELECT target.benchmark_version_id, target.skill_id, target.skill_code, target.skill_name,
                   target.competency_name, target.practice_name, target.proficiency_rank,
                   target.proficiency_code, target.proficiency_name
            FROM reference.benchmark_target AS target
            JOIN reference.benchmark_version AS version ON version.benchmark_version_id = target.benchmark_version_id
            WHERE {where.Replace("job_profile_id", "version.job_profile_id", StringComparison.Ordinal)}
            ORDER BY target.skill_name
            """,
            connection))
        {
            if (jobProfileId is Guid profileId)
                command.Parameters.AddWithValue(profileId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var versionId = reader.GetGuid(0);
                if (!targets.TryGetValue(versionId, out var list))
                {
                    list = [];
                    targets[versionId] = list;
                }

                list.Add(new TargetView(
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetString(7),
                    reader.GetString(8)));
            }
        }

        return versions.Select(version => version with
        {
            Targets = targets.TryGetValue(version.BenchmarkVersionId, out var list) ? list : []
        }).ToArray();
    }

    private static async Task CopyTaxonomy(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string payload,
        CancellationToken cancellationToken)
    {
        var publication = JsonSerializer.Deserialize<TaxonomyPublication>(payload, Json)
            ?? throw new InvalidOperationException("The taxonomy publication could not be read.");
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO reference.taxonomy_version (
                taxonomy_version_id, version_number, status, published_at, superseded_by)
            VALUES ($1, $2, 'published', $3, NULL)
            ON CONFLICT (taxonomy_version_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(publication.TaxonomyVersionId);
            command.Parameters.AddWithValue(publication.VersionNumber);
            command.Parameters.AddWithValue(publication.PublishedAt);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var skill in publication.Skills)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO reference.taxonomy_skill (
                    taxonomy_version_id, skill_id, skill_code, skill_name,
                    competency_code, competency_name, practice_code, practice_name)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
                ON CONFLICT (taxonomy_version_id, skill_id) DO NOTHING
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue(publication.TaxonomyVersionId);
            command.Parameters.AddWithValue(skill.SkillId);
            command.Parameters.AddWithValue(skill.SkillCode);
            command.Parameters.AddWithValue(skill.SkillName);
            command.Parameters.AddWithValue(skill.CompetencyCode);
            command.Parameters.AddWithValue(skill.CompetencyName);
            command.Parameters.AddWithValue(skill.PracticeCode);
            command.Parameters.AddWithValue(skill.PracticeName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var proficiency in publication.Proficiencies)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO reference.proficiency (taxonomy_version_id, code, display_name, rank)
                VALUES ($1, $2, $3, $4)
                ON CONFLICT (taxonomy_version_id, rank) DO NOTHING
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue(publication.TaxonomyVersionId);
            command.Parameters.AddWithValue(proficiency.Code);
            command.Parameters.AddWithValue(proficiency.Name);
            command.Parameters.AddWithValue(proficiency.Rank);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (publication.Supersedes is Guid previous)
            await SupersedeTaxonomy(connection, transaction, previous, publication.TaxonomyVersionId, publication.PublishedAt, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyBenchmark(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string payload,
        CancellationToken cancellationToken)
    {
        var publication = JsonSerializer.Deserialize<BenchmarkPublication>(payload, Json)
            ?? throw new InvalidOperationException("The benchmark publication could not be read.");
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO reference.benchmark_version (
                benchmark_version_id, job_profile_id, profile_code, profile_name,
                framework_code, framework_name, level_code, level_name, level_rank,
                taxonomy_version_id, version_number, status, supersedes_version_id, published_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, 'published', $12, $13)
            ON CONFLICT (benchmark_version_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(publication.BenchmarkVersionId);
            command.Parameters.AddWithValue(publication.JobProfileId);
            command.Parameters.AddWithValue(publication.ProfileCode);
            command.Parameters.AddWithValue(publication.ProfileName);
            command.Parameters.AddWithValue(publication.FrameworkCode);
            command.Parameters.AddWithValue(publication.FrameworkName);
            command.Parameters.AddWithValue(publication.LevelCode);
            command.Parameters.AddWithValue(publication.LevelName);
            command.Parameters.AddWithValue(publication.LevelRank);
            command.Parameters.AddWithValue(publication.TaxonomyVersionId);
            command.Parameters.AddWithValue(publication.VersionNumber);
            command.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid,
                Value = (object?)publication.Supersedes ?? DBNull.Value
            });
            command.Parameters.AddWithValue(publication.PublishedAt);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var target in publication.Targets)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO reference.benchmark_target (
                    benchmark_version_id, skill_id, skill_code, skill_name, competency_name, practice_name,
                    proficiency_rank, proficiency_code, proficiency_name)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                ON CONFLICT (benchmark_version_id, skill_id) DO NOTHING
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue(publication.BenchmarkVersionId);
            command.Parameters.AddWithValue(target.SkillId);
            command.Parameters.AddWithValue(target.SkillCode);
            command.Parameters.AddWithValue(target.SkillName);
            command.Parameters.AddWithValue(target.CompetencyName);
            command.Parameters.AddWithValue(target.PracticeName);
            command.Parameters.AddWithValue(target.ProficiencyRank);
            command.Parameters.AddWithValue(target.ProficiencyCode);
            command.Parameters.AddWithValue(target.ProficiencyName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (publication.Supersedes is Guid previous)
            await SupersedeBenchmark(connection, transaction, previous, publication.BenchmarkVersionId, publication.PublishedAt, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SupersedeTaxonomy(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid previousId,
        Guid successorId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE reference.taxonomy_version
            SET status = 'superseded', superseded_at = $3, superseded_by = $2
            WHERE taxonomy_version_id = $1 AND status = 'published'
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(previousId);
        command.Parameters.AddWithValue(successorId);
        command.Parameters.AddWithValue(at);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SupersedeBenchmark(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid previousId,
        Guid successorId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE reference.benchmark_version
            SET status = 'superseded', superseded_at = $3, superseded_by = $2
            WHERE benchmark_version_id = $1 AND status = 'published'
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(previousId);
        command.Parameters.AddWithValue(successorId);
        command.Parameters.AddWithValue(at);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
