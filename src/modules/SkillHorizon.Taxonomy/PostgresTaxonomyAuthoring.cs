using System.Text.Json;
using Npgsql;
using SkillHorizon.Audit;
using SkillHorizon.Messaging;

namespace SkillHorizon.Taxonomy;

public sealed class PostgresTaxonomyAuthoring : ITaxonomyAuthoring
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;
    private readonly IOutbox _outbox;

    public PostgresTaxonomyAuthoring(NpgsqlDataSource dataSource, IAuditLog audit, IOutbox outbox)
    {
        _dataSource = dataSource;
        _audit = audit;
        _outbox = outbox;
    }

    public async Task<TaxonomyVersionView> SaveDraftAsync(TaxonomyDraft draft, CancellationToken cancellationToken)
    {
        var practices = Normalize(draft);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var versionId = await OpenDraft(connection, transaction, cancellationToken).ConfigureAwait(false);
        await ClearSnapshot(connection, transaction, versionId, cancellationToken).ConfigureAwait(false);
        foreach (var practice in practices)
        {
            var practiceId = await UpsertPractice(connection, transaction, practice, cancellationToken).ConfigureAwait(false);
            foreach (var competency in practice.Competencies)
            {
                var competencyId = await UpsertCompetency(connection, transaction, practiceId, competency, cancellationToken).ConfigureAwait(false);
                foreach (var skill in competency.Skills)
                {
                    var skillId = await UpsertSkill(connection, transaction, competencyId, skill, cancellationToken).ConfigureAwait(false);
                    await InsertSnapshot(connection, transaction, versionId, skillId, skill, competency, practice, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        foreach (var proficiency in draft.Proficiencies.OrderBy(item => item.Rank))
        {
            await InsertProficiency(connection, transaction, versionId, proficiency, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await FindAsync(versionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The taxonomy draft was not saved.");
    }

    public async Task<TaxonomyVersionView> PublishAsync(
        Guid taxonomyVersionId,
        string subjectId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var draft = await LockVersion(connection, transaction, taxonomyVersionId, cancellationToken).ConfigureAwait(false);
        if (draft is null || draft.Value.Status != "draft")
            throw new InvalidOperationException("Only a taxonomy draft can be published.");
        if (draft.Value.SkillCount == 0 || draft.Value.ProficiencyCount == 0)
            throw new InvalidOperationException("A taxonomy version needs skills and a proficiency scale before it can be published.");

        var previous = await LockPublished(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (previous is not null)
            await Supersede(connection, transaction, previous.Value, taxonomyVersionId, cancellationToken).ConfigureAwait(false);

        var publishedAt = await MarkPublished(connection, transaction, taxonomyVersionId, cancellationToken).ConfigureAwait(false);
        var view = await ReadVersion(connection, transaction, taxonomyVersionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("That taxonomy version does not exist.");
        var publication = new TaxonomyPublication(
            "taxonomy",
            view.TaxonomyVersionId,
            view.VersionNumber,
            publishedAt,
            previous,
            view.Skills.Select(skill => new PublishedSkill(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.CompetencyCode,
                skill.CompetencyName,
                skill.PracticeCode,
                skill.PracticeName)).ToArray(),
            view.Proficiencies.Select(item => new PublishedProficiency(item.Code, item.Name, item.Rank)).ToArray());
        var payload = JsonSerializer.Serialize(publication, Json);
        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(subjectId, TaxonomyModule.PublishedMessage, "taxonomy.version", taxonomyVersionId, correlationId, payload),
            cancellationToken).ConfigureAwait(false);
        await _outbox.EnqueueAsync(
            connection,
            transaction,
            new OutboxDraft(TaxonomyModule.PublishedMessage, payload, correlationId),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return view with { Status = "published" };
    }

    public async Task<TaxonomyVersionView?> FindAsync(Guid taxonomyVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadVersion(connection, null, taxonomyVersionId, cancellationToken).ConfigureAwait(false);
    }

    private static List<PracticeDraft> Normalize(TaxonomyDraft draft)
    {
        if (draft.Practices.Count == 0)
            throw new InvalidOperationException("A taxonomy draft needs a practice.");
        if (draft.Proficiencies.Count == 0)
            throw new InvalidOperationException("A taxonomy draft needs a proficiency scale.");

        var ranks = new HashSet<int>();
        var proficiencyCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proficiency in draft.Proficiencies)
        {
            var code = proficiency.Code.Trim();
            if (code.Length == 0 || proficiency.Name.Trim().Length == 0 || proficiency.Rank < 1)
                throw new InvalidOperationException("Each proficiency needs a code, a name, and a positive rank.");
            if (!ranks.Add(proficiency.Rank) || !proficiencyCodes.Add(code))
                throw new InvalidOperationException("Proficiency codes and ranks must be unique in a taxonomy version.");
        }

        var practiceCodes = new HashSet<string>(StringComparer.Ordinal);
        var competencyCodes = new HashSet<string>(StringComparer.Ordinal);
        var skillCodes = new HashSet<string>(StringComparer.Ordinal);
        var practices = new List<PracticeDraft>();
        foreach (var practice in draft.Practices)
        {
            var practiceCode = practice.Code.Trim();
            if (practiceCode.Length == 0 || practice.Name.Trim().Length == 0 || practice.Competencies.Count == 0)
                throw new InvalidOperationException("Each practice needs a code, a name, and a competency.");
            if (!practiceCodes.Add(practiceCode))
                throw new InvalidOperationException($"Practice '{practiceCode}' is repeated.");

            var competencies = new List<CompetencyDraft>();
            foreach (var competency in practice.Competencies)
            {
                var competencyCode = competency.Code.Trim();
                if (competencyCode.Length == 0 || competency.Name.Trim().Length == 0 || competency.Skills.Count == 0)
                    throw new InvalidOperationException("Each competency needs a code, a name, and a skill.");
                if (!competencyCodes.Add(competencyCode))
                    throw new InvalidOperationException($"Competency '{competencyCode}' is repeated.");

                var skills = new List<SkillDraft>();
                foreach (var skill in competency.Skills)
                {
                    var skillCode = skill.Code.Trim();
                    if (skillCode.Length == 0 || skill.Name.Trim().Length == 0)
                        throw new InvalidOperationException("Each skill needs a code and a name.");
                    if (!skillCodes.Add(skillCode))
                        throw new InvalidOperationException($"Skill '{skillCode}' is repeated.");
                    skills.Add(new SkillDraft(skillCode, skill.Name.Trim()));
                }

                competencies.Add(new CompetencyDraft(competencyCode, competency.Name.Trim(), skills));
            }

            practices.Add(new PracticeDraft(practiceCode, practice.Name.Trim(), competencies));
        }

        return practices;
    }

    private static async Task<Guid> OpenDraft(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var existing = new NpgsqlCommand(
            "SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft' FOR UPDATE",
            connection,
            transaction);
        var found = await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is Guid versionId)
            return versionId;

        var created = Guid.NewGuid();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.version (taxonomy_version_id, version_number, status, created_at)
            VALUES (
                $1,
                (SELECT COALESCE(MAX(version_number), 0) + 1 FROM taxonomy.version),
                'draft',
                now())
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue(created);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    private static async Task ClearSnapshot(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        await using var skills = new NpgsqlCommand(
            "DELETE FROM taxonomy.version_skill WHERE taxonomy_version_id = $1",
            connection,
            transaction);
        skills.Parameters.AddWithValue(versionId);
        await skills.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var scale = new NpgsqlCommand(
            "DELETE FROM taxonomy.proficiency WHERE taxonomy_version_id = $1",
            connection,
            transaction);
        scale.Parameters.AddWithValue(versionId);
        await scale.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Guid> UpsertPractice(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PracticeDraft practice,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.practice (practice_id, code, display_name)
            VALUES ($1, $2, $3)
            ON CONFLICT (code) DO UPDATE SET display_name = EXCLUDED.display_name
            RETURNING practice_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(practice.Code);
        command.Parameters.AddWithValue(practice.Name);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task<Guid> UpsertCompetency(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid practiceId,
        CompetencyDraft competency,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.competency (competency_id, practice_id, code, display_name)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (code) DO UPDATE
                SET display_name = EXCLUDED.display_name,
                    practice_id = EXCLUDED.practice_id
            RETURNING competency_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(practiceId);
        command.Parameters.AddWithValue(competency.Code);
        command.Parameters.AddWithValue(competency.Name);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task<Guid> UpsertSkill(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid competencyId,
        SkillDraft skill,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.skill (skill_id, competency_id, code, display_name)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (code) DO UPDATE
                SET display_name = EXCLUDED.display_name,
                    competency_id = EXCLUDED.competency_id
            RETURNING skill_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(competencyId);
        command.Parameters.AddWithValue(skill.Code);
        command.Parameters.AddWithValue(skill.Name);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task InsertSnapshot(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        Guid skillId,
        SkillDraft skill,
        CompetencyDraft competency,
        PracticeDraft practice,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.version_skill (
                taxonomy_version_id, skill_id, skill_code, skill_name,
                competency_code, competency_name, practice_code, practice_name)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(versionId);
        command.Parameters.AddWithValue(skillId);
        command.Parameters.AddWithValue(skill.Code);
        command.Parameters.AddWithValue(skill.Name);
        command.Parameters.AddWithValue(competency.Code);
        command.Parameters.AddWithValue(competency.Name);
        command.Parameters.AddWithValue(practice.Code);
        command.Parameters.AddWithValue(practice.Name);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertProficiency(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        ProficiencyDraft proficiency,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.proficiency (taxonomy_version_id, code, display_name, rank)
            VALUES ($1, $2, $3, $4)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(versionId);
        command.Parameters.AddWithValue(proficiency.Code.Trim());
        command.Parameters.AddWithValue(proficiency.Name.Trim());
        command.Parameters.AddWithValue(proficiency.Rank);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string Status, int SkillCount, int ProficiencyCount)?> LockVersion(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT status,
                   (SELECT count(*) FROM taxonomy.version_skill WHERE taxonomy_version_id = version.taxonomy_version_id)::int,
                   (SELECT count(*) FROM taxonomy.proficiency WHERE taxonomy_version_id = version.taxonomy_version_id)::int
            FROM taxonomy.version
            WHERE taxonomy_version_id = $1
            FOR UPDATE
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(versionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private static async Task<Guid?> LockPublished(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'published' FOR UPDATE",
            connection,
            transaction);
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
            UPDATE taxonomy.version
            SET status = 'superseded', superseded_at = now(), superseded_by = $2
            WHERE taxonomy_version_id = $1 AND status = 'published'
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
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE taxonomy.version
            SET status = 'published', published_at = now()
            WHERE taxonomy_version_id = $1 AND status = 'draft'
            RETURNING published_at
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(versionId);
        var publishedAt = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (publishedAt is not DateTime stamp)
            throw new InvalidOperationException("Only a taxonomy draft can be published.");
        return new DateTimeOffset(DateTime.SpecifyKind(stamp, DateTimeKind.Utc));
    }

    private static async Task<TaxonomyVersionView?> ReadVersion(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        int versionNumber;
        string status;
        await using (var header = new NpgsqlCommand(
            """
            SELECT version_number, status
            FROM taxonomy.version
            WHERE taxonomy_version_id = $1
            """,
            connection,
            transaction))
        {
            header.Parameters.AddWithValue(versionId);
            await using var headerReader = await header.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await headerReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            versionNumber = headerReader.GetInt32(0);
            status = headerReader.GetString(1);
        }

        var skills = new List<TaxonomySkillView>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT skill_id, skill_code, skill_name, competency_code, competency_name, practice_code, practice_name
            FROM taxonomy.version_skill
            WHERE taxonomy_version_id = $1
            ORDER BY practice_name, competency_name, skill_name
            """,
            connection,
            transaction))
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

        var scale = new List<ProficiencyView>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT code, display_name, rank
            FROM taxonomy.proficiency
            WHERE taxonomy_version_id = $1
            ORDER BY rank
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(versionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                scale.Add(new ProficiencyView(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return new TaxonomyVersionView(versionId, versionNumber, status, skills, scale);
    }
}
