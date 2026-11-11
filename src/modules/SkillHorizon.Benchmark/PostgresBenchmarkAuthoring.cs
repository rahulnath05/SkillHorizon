using System.Text.Json;
using Npgsql;
using SkillHorizon.Audit;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Messaging;
using SkillHorizon.Readiness;
using SkillHorizon.Taxonomy;

namespace SkillHorizon.Benchmark;

public sealed class PostgresBenchmarkAuthoring : IBenchmarkAuthoring
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly IJobArchitecture _jobs;
    private readonly ITaxonomyAuthoring _taxonomy;
    private readonly IAuditLog _audit;
    private readonly IOutbox _outbox;
    private readonly IPracticeProjection _practice;

    public PostgresBenchmarkAuthoring(
        NpgsqlDataSource dataSource,
        IJobArchitecture jobs,
        ITaxonomyAuthoring taxonomy,
        IAuditLog audit,
        IOutbox outbox,
        IPracticeProjection practice)
    {
        _dataSource = dataSource;
        _jobs = jobs;
        _taxonomy = taxonomy;
        _audit = audit;
        _outbox = outbox;
        _practice = practice;
    }

    public async Task<BenchmarkVersionView> SaveDraftAsync(BenchmarkDraftWrite draft, CancellationToken cancellationToken)
    {
        var profile = await _jobs.FindProfileAsync(draft.JobProfileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("That job profile does not exist.");
        var taxonomy = await _taxonomy.FindAsync(draft.TaxonomyVersionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("That taxonomy version does not exist.");
        if (taxonomy.Status == "draft")
            throw new InvalidOperationException("A benchmark is pinned to a published taxonomy version.");
        var targets = ResolveTargets(draft.Targets, taxonomy);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var versionId = await OpenDraft(connection, transaction, profile.ProfileId, taxonomy.TaxonomyVersionId, cancellationToken).ConfigureAwait(false);
        await ReplaceTargets(connection, transaction, versionId, targets, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ReadOne(versionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BenchmarkVersionView> PublishAsync(
        Guid benchmarkVersionId,
        string subjectId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var locked = await LockDraft(connection, transaction, benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        if (locked is null)
            throw new InvalidOperationException("Only a benchmark draft can be published.");

        var taxonomy = await _taxonomy.FindAsync(locked.Value.TaxonomyVersionId, cancellationToken).ConfigureAwait(false);
        if (taxonomy is null || taxonomy.Status == "draft")
            throw new InvalidOperationException("A benchmark is pinned to a published taxonomy version.");
        if (locked.Value.TargetCount == 0)
            throw new InvalidOperationException("A benchmark needs at least one skill target before it can be published.");

        var previous = await LockPublished(connection, transaction, locked.Value.JobProfileId, cancellationToken).ConfigureAwait(false);
        if (previous is not null)
            await Supersede(connection, transaction, previous.Value, benchmarkVersionId, cancellationToken).ConfigureAwait(false);

        var publishedAt = await MarkPublished(connection, transaction, benchmarkVersionId, previous, cancellationToken).ConfigureAwait(false);
        var publication = await ReadPublication(connection, transaction, benchmarkVersionId, publishedAt, previous, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(publication, Json);
        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(subjectId, BenchmarkModule.PublishedMessage, "benchmark.version", benchmarkVersionId, correlationId, payload),
            cancellationToken).ConfigureAwait(false);
        await _outbox.EnqueueAsync(
            connection,
            transaction,
            new OutboxDraft(BenchmarkModule.PublishedMessage, payload, correlationId),
            cancellationToken).ConfigureAwait(false);
        await _practice.MarkDirtyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ReadOne(benchmarkVersionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BenchmarkVersionView>> ListDraftsAsync(CancellationToken cancellationToken) =>
        await List("v.status = 'draft'", null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<BenchmarkVersionView>> PublishedAsync(CancellationToken cancellationToken) =>
        await List("v.status IN ('published', 'superseded')", null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<BenchmarkVersionView>> HistoryAsync(Guid jobProfileId, CancellationToken cancellationToken) =>
        await List("v.job_profile_id = $1 AND v.status IN ('published', 'superseded')", jobProfileId, cancellationToken).ConfigureAwait(false);

    private static List<TargetView> ResolveTargets(IReadOnlyList<TargetDraft> requested, TaxonomyVersionView taxonomy)
    {
        if (requested.Count == 0)
            throw new InvalidOperationException("A benchmark needs at least one skill target.");

        var skills = taxonomy.Skills.ToDictionary(skill => skill.SkillId);
        var scale = taxonomy.Proficiencies.ToDictionary(item => item.Rank);
        var seen = new HashSet<Guid>();
        var targets = new List<TargetView>();
        foreach (var target in requested)
        {
            if (!seen.Add(target.SkillId))
                throw new InvalidOperationException("A skill can have only one target on a benchmark version.");
            if (!skills.TryGetValue(target.SkillId, out var skill))
                throw new InvalidOperationException("A benchmark target must be a skill on the pinned taxonomy version.");
            if (!scale.TryGetValue(target.ProficiencyRank, out var proficiency))
                throw new InvalidOperationException("A benchmark target must use a proficiency on the pinned taxonomy version.");
            targets.Add(new TargetView(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.CompetencyName,
                skill.PracticeName,
                proficiency.Rank,
                proficiency.Code,
                proficiency.Name));
        }

        return targets;
    }

    private static async Task<Guid> OpenDraft(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid profileId,
        Guid taxonomyVersionId,
        CancellationToken cancellationToken)
    {
        await using var existing = new NpgsqlCommand(
            """
            SELECT benchmark_version_id
            FROM benchmark.version
            WHERE job_profile_id = $1 AND status = 'draft'
            FOR UPDATE
            """,
            connection,
            transaction);
        existing.Parameters.AddWithValue(profileId);
        var found = await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is Guid versionId)
        {
            await using var retarget = new NpgsqlCommand(
                """
                UPDATE benchmark.version
                SET taxonomy_version_id = $2
                WHERE benchmark_version_id = $1 AND status = 'draft'
                """,
                connection,
                transaction);
            retarget.Parameters.AddWithValue(versionId);
            retarget.Parameters.AddWithValue(taxonomyVersionId);
            await retarget.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return versionId;
        }

        var created = Guid.NewGuid();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO benchmark.version (
                benchmark_version_id, job_profile_id, taxonomy_version_id, version_number, status, created_at)
            VALUES (
                $1,
                $2,
                $3,
                (SELECT COALESCE(MAX(version_number), 0) + 1 FROM benchmark.version WHERE job_profile_id = $2),
                'draft',
                now())
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue(created);
        insert.Parameters.AddWithValue(profileId);
        insert.Parameters.AddWithValue(taxonomyVersionId);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    private static async Task ReplaceTargets(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        IReadOnlyList<TargetView> targets,
        CancellationToken cancellationToken)
    {
        await using var clear = new NpgsqlCommand(
            "DELETE FROM benchmark.target WHERE benchmark_version_id = $1",
            connection,
            transaction);
        clear.Parameters.AddWithValue(versionId);
        await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        foreach (var target in targets)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO benchmark.target (
                    benchmark_version_id, skill_id, skill_code, skill_name, competency_name, practice_name,
                    proficiency_rank, proficiency_code, proficiency_name)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue(versionId);
            insert.Parameters.AddWithValue(target.SkillId);
            insert.Parameters.AddWithValue(target.SkillCode);
            insert.Parameters.AddWithValue(target.SkillName);
            insert.Parameters.AddWithValue(target.CompetencyName);
            insert.Parameters.AddWithValue(target.PracticeName);
            insert.Parameters.AddWithValue(target.ProficiencyRank);
            insert.Parameters.AddWithValue(target.ProficiencyCode);
            insert.Parameters.AddWithValue(target.ProficiencyName);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<(Guid JobProfileId, Guid TaxonomyVersionId, int TargetCount)?> LockDraft(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT job_profile_id, taxonomy_version_id,
                   (SELECT count(*) FROM benchmark.target WHERE benchmark_version_id = version.benchmark_version_id)::int
            FROM benchmark.version
            WHERE benchmark_version_id = $1 AND status = 'draft'
            FOR UPDATE
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(versionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2));
    }

    private static async Task<Guid?> LockPublished(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT benchmark_version_id
            FROM benchmark.version
            WHERE job_profile_id = $1 AND status = 'published'
            FOR UPDATE
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(profileId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is Guid versionId ? versionId : null;
    }

    private static async Task Supersede(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid previousId,
        Guid successorId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE benchmark.version
            SET status = 'superseded', superseded_at = now(), superseded_by = $2
            WHERE benchmark_version_id = $1 AND status = 'published'
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(previousId);
        command.Parameters.AddWithValue(successorId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DateTimeOffset> MarkPublished(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        Guid? supersedes,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE benchmark.version
            SET status = 'published', published_at = now(), supersedes_version_id = $2
            WHERE benchmark_version_id = $1 AND status = 'draft'
            RETURNING published_at
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(versionId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid,
            Value = (object?)supersedes ?? DBNull.Value
        });
        var publishedAt = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (publishedAt is not DateTime stamp)
            throw new InvalidOperationException("Only a benchmark draft can be published.");
        return new DateTimeOffset(DateTime.SpecifyKind(stamp, DateTimeKind.Utc));
    }

    private static async Task<BenchmarkPublication> ReadPublication(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        DateTimeOffset publishedAt,
        Guid? supersedes,
        CancellationToken cancellationToken)
    {
        Guid profileId;
        string profileCode;
        string profileName;
        string frameworkCode;
        string frameworkName;
        string levelCode;
        string levelName;
        int levelRank;
        Guid taxonomyVersionId;
        int versionNumber;
        await using (var header = new NpgsqlCommand(
            """
            SELECT v.job_profile_id, profile.code, profile.display_name,
                   framework.code, framework.display_name, level.code, level.display_name, level.rank,
                   v.taxonomy_version_id, v.version_number
            FROM benchmark.version AS v
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = v.job_profile_id
            JOIN job_architecture.framework AS framework ON framework.framework_id = profile.framework_id
            JOIN job_architecture.level AS level ON level.level_id = profile.level_id
            WHERE v.benchmark_version_id = $1
            """,
            connection,
            transaction))
        {
            header.Parameters.AddWithValue(versionId);
            await using var reader = await header.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("That benchmark version does not exist.");
            profileId = reader.GetGuid(0);
            profileCode = reader.GetString(1);
            profileName = reader.GetString(2);
            frameworkCode = reader.GetString(3);
            frameworkName = reader.GetString(4);
            levelCode = reader.GetString(5);
            levelName = reader.GetString(6);
            levelRank = reader.GetInt32(7);
            taxonomyVersionId = reader.GetGuid(8);
            versionNumber = reader.GetInt32(9);
        }

        var targets = new List<PublishedTarget>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT skill_id, skill_code, skill_name, competency_name, practice_name,
                   proficiency_rank, proficiency_code, proficiency_name
            FROM benchmark.target
            WHERE benchmark_version_id = $1
            ORDER BY skill_name
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(versionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                targets.Add(new PublishedTarget(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetString(7)));
            }
        }

        return new BenchmarkPublication(
            "benchmark",
            versionId,
            profileId,
            profileCode,
            profileName,
            frameworkCode,
            frameworkName,
            levelCode,
            levelName,
            levelRank,
            taxonomyVersionId,
            versionNumber,
            publishedAt,
            supersedes,
            targets);
    }

    private async Task<BenchmarkVersionView> ReadOne(Guid versionId, CancellationToken cancellationToken)
    {
        var versions = await List("v.benchmark_version_id = $1", versionId, cancellationToken).ConfigureAwait(false);
        return versions.Single();
    }

    private async Task<IReadOnlyList<BenchmarkVersionView>> List(
        string where,
        Guid? parameter,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var versions = new List<BenchmarkVersionView>();
        await using (var command = new NpgsqlCommand(
            $"""
            SELECT v.benchmark_version_id, v.job_profile_id, profile.code, profile.display_name,
                   framework.code, framework.display_name, level.code, level.display_name, level.rank,
                   v.taxonomy_version_id, v.version_number, v.status, v.supersedes_version_id, v.superseded_by
            FROM benchmark.version AS v
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = v.job_profile_id
            JOIN job_architecture.framework AS framework ON framework.framework_id = profile.framework_id
            JOIN job_architecture.level AS level ON level.level_id = profile.level_id
            WHERE {where}
            ORDER BY v.version_number, profile.display_name
            """,
            connection))
        {
            if (parameter is Guid value)
                command.Parameters.AddWithValue(value);
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
            SELECT t.benchmark_version_id, t.skill_id, t.skill_code, t.skill_name, t.competency_name, t.practice_name,
                   t.proficiency_rank, t.proficiency_code, t.proficiency_name
            FROM benchmark.target AS t
            JOIN benchmark.version AS v ON v.benchmark_version_id = t.benchmark_version_id
            WHERE {where}
            ORDER BY t.skill_name
            """,
            connection))
        {
            if (parameter is Guid value)
                command.Parameters.AddWithValue(value);
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
}
