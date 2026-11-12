using Npgsql;

namespace SkillHorizon.Api;

public sealed class LdBoardFixture
{
    private static readonly Guid StorySkillId = Guid.Parse("c33c0001-0000-4000-8000-0000000000a1");
    private static readonly Guid StewardProfileId = Guid.Parse("c33c0001-0000-4000-8000-0000000000b1");
    private static readonly Guid StewardVersionId = Guid.Parse("c33c0001-0000-4000-8000-0000000000b2");
    private static readonly Guid StewardCompetencyId = Guid.Parse("c33c0001-0000-4000-8000-0000000000c1");
    private static readonly Guid OverlapId = Guid.Parse("c33c0001-0000-4000-8000-0000000000d1");
    private static readonly Guid AliasId = Guid.Parse("c33c0001-0000-4000-8000-0000000000e1");
    private static readonly Guid DraftVersionId = Guid.Parse("c33c0001-0000-4000-8000-0000000000f1");

    private readonly NpgsqlDataSource _dataSource;
    private readonly string _zoneId;

    public LdBoardFixture(NpgsqlDataSource dataSource, string zoneId)
    {
        _dataSource = dataSource;
        _zoneId = zoneId;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnrichSkills(connection, cancellationToken).ConfigureAwait(false);
        await EnrichProfiles(connection, cancellationToken).ConfigureAwait(false);
        await EnsureStewardship(connection, cancellationToken).ConfigureAwait(false);
        await EnrichRequests(connection, cancellationToken).ConfigureAwait(false);
        await EnsureNotes(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnrichSkills(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
            """
            UPDATE taxonomy.skill
            SET category = CASE code
                    WHEN 'sql' THEN 'Analytics engineering'
                    WHEN 'modeling' THEN 'Analytics engineering'
                    ELSE category
                END,
                definition = CASE code
                    WHEN 'sql' THEN 'Reads and shapes tabular data so a result can be checked.'
                    WHEN 'modeling' THEN 'Structures entities and relationships so a report stays consistent.'
                    ELSE definition
                END,
                status = CASE code WHEN 'modeling' THEN 'draft' ELSE 'active' END
            WHERE code IN ('sql', 'modeling')
            """,
            connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var draftId = await EnsureDraft(connection, cancellationToken).ConfigureAwait(false);
        if (draftId is null)
            return;

        await using (var command = new NpgsqlCommand(
            """
            UPDATE taxonomy.version_skill AS snapshot
            SET category = skill.category,
                definition = skill.definition,
                status = skill.status
            FROM taxonomy.skill AS skill
            WHERE snapshot.taxonomy_version_id = $1
              AND snapshot.skill_code = skill.code
            """,
            connection))
        {
            command.Parameters.AddWithValue(draftId.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            UPDATE taxonomy.proficiency
            SET meaning = CASE rank
                WHEN 1 THEN 'Can follow a worked example with guidance.'
                WHEN 2 THEN 'Can do the work on a familiar problem.'
                WHEN 3 THEN 'Can do the work when the problem is new.'
                WHEN 4 THEN 'Can set the approach for other people.'
                ELSE meaning
            END
            WHERE taxonomy_version_id = $1
            """,
            connection))
        {
            command.Parameters.AddWithValue(draftId.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.skill (skill_id, competency_id, code, display_name, category, definition, status)
            SELECT $1, competency_id, 'storytelling', 'Storytelling', 'Communication',
                   'Explains a result so a decision maker can use it.', 'active'
            FROM taxonomy.competency
            WHERE code = 'analysis'
            ON CONFLICT (code) DO UPDATE SET
                category = EXCLUDED.category,
                definition = EXCLUDED.definition,
                status = EXCLUDED.status
            """,
            connection))
        {
            command.Parameters.AddWithValue(StorySkillId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.version_skill (
                taxonomy_version_id, skill_id, skill_code, skill_name, competency_code, competency_name,
                practice_code, practice_name, category, definition, status)
            SELECT version.taxonomy_version_id, skill.skill_id, skill.code, skill.display_name,
                   competency.code, competency.display_name, practice.code, practice.display_name,
                   skill.category, skill.definition, skill.status
            FROM taxonomy.version AS version
            JOIN taxonomy.skill AS skill ON skill.code = 'storytelling'
            JOIN taxonomy.competency AS competency ON competency.competency_id = skill.competency_id
            JOIN taxonomy.practice AS practice ON practice.practice_id = competency.practice_id
            WHERE version.status = 'draft'
            ON CONFLICT (taxonomy_version_id, skill_id) DO UPDATE SET
                category = EXCLUDED.category,
                definition = EXCLUDED.definition,
                status = EXCLUDED.status
            """,
            connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = new NpgsqlCommand(
            """
            UPDATE taxonomy.competency
            SET com_lead = COALESCE((SELECT display_name FROM workforce.person WHERE employee_id = 'person-lead'), 'person-lead'),
                approval_status = 'review'
            WHERE code = 'analysis'
            """,
            connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Guid?> EnsureDraft(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var existing = new NpgsqlCommand("SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft' LIMIT 1", connection))
        {
            var found = await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is Guid id)
                return id;
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.version (taxonomy_version_id, version_number, status, created_at)
            SELECT $1, COALESCE(MAX(version_number), 0) + 1, 'draft', now()
            FROM taxonomy.version
            WHERE EXISTS (SELECT 1 FROM taxonomy.version WHERE status = 'published')
            """,
            connection))
        {
            command.Parameters.AddWithValue(DraftVersionId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                return null;
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.version_skill (
                taxonomy_version_id, skill_id, skill_code, skill_name, competency_code, competency_name,
                practice_code, practice_name, category, definition, status)
            SELECT $1, skill_id, skill_code, skill_name, competency_code, competency_name,
                   practice_code, practice_name, '', '', 'active'
            FROM taxonomy.version_skill
            WHERE taxonomy_version_id = (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'published' LIMIT 1)
            ON CONFLICT (taxonomy_version_id, skill_id) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue(DraftVersionId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.proficiency (taxonomy_version_id, code, display_name, rank, meaning)
            SELECT $1, code, display_name, rank, ''
            FROM taxonomy.proficiency
            WHERE taxonomy_version_id = (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'published' LIMIT 1)
            ON CONFLICT (taxonomy_version_id, rank) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue(DraftVersionId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return DraftVersionId;
    }

    private static async Task EnrichProfiles(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
            """
            UPDATE job_architecture.job_profile
            SET purpose = CASE code
                    WHEN 'data-analyst' THEN 'Turns a business question into a checked dataset and a short readout.'
                    WHEN 'data-consultant' THEN 'Sets the analytical approach for a client team.'
                    ELSE purpose
                END,
                band = CASE code WHEN 'data-consultant' THEN 'Consultant' ELSE 'Associate' END,
                family_name = 'Consulting',
                competency_code = 'analysis'
            WHERE code IN ('data-analyst', 'data-consultant')
            """,
            connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = new NpgsqlCommand(
            """
            UPDATE benchmark.target AS target
            SET suggested_rank = target.proficiency_rank
            FROM benchmark.version AS version
            WHERE version.benchmark_version_id = target.benchmark_version_id
              AND version.status = 'draft'
              AND target.suggested_rank IS NULL
            """,
            connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureStewardship(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.competency (competency_id, practice_id, code, display_name, com_lead, approval_status)
            SELECT $1, practice_id, 'stewardship', 'Stewardship',
                   COALESCE((SELECT display_name FROM workforce.person WHERE employee_id = 'person-lead'), 'person-lead'),
                   'draft'
            FROM taxonomy.practice
            WHERE code = 'data'
            ON CONFLICT (code) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue(StewardCompetencyId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO job_architecture.job_profile (
                profile_id, framework_id, level_id, code, display_name, purpose, band, family_name, competency_code)
            SELECT $1, framework.framework_id, level.level_id, 'data-steward', 'Data steward',
                   'Keeps the definitions behind a published dataset.', 'Associate', 'Consulting', 'stewardship'
            FROM job_architecture.framework AS framework
            JOIN job_architecture.level AS level ON level.framework_id = framework.framework_id AND level.code = 'associate'
            WHERE framework.code = 'consulting'
            ON CONFLICT (code) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue(StewardProfileId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        Guid? taxonomyId = null;
        await using (var command = new NpgsqlCommand(
            "SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'published' LIMIT 1",
            connection))
        {
            var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is Guid id)
                taxonomyId = id;
        }

        if (taxonomyId is null)
            return;

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO benchmark.version (
                benchmark_version_id, job_profile_id, taxonomy_version_id, version_number, status, created_at)
            SELECT $1, profile.profile_id, $2, 1, 'draft', now()
            FROM job_architecture.job_profile AS profile
            WHERE profile.code = 'data-steward'
              AND NOT EXISTS (
                  SELECT 1 FROM benchmark.version AS version
                  WHERE version.job_profile_id = profile.profile_id)
            """,
            connection))
        {
            command.Parameters.AddWithValue(StewardVersionId);
            command.Parameters.AddWithValue(taxonomyId.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO benchmark.target (
                benchmark_version_id, skill_id, skill_code, skill_name, competency_name, practice_name,
                proficiency_rank, proficiency_code, proficiency_name, suggested_rank, level_set)
            SELECT version.benchmark_version_id, skill.skill_id, skill.skill_code, skill.skill_name,
                   skill.competency_name, skill.practice_name,
                   proficiency.rank, proficiency.code, proficiency.display_name, proficiency.rank,
                   skill.skill_code <> 'modeling'
            FROM benchmark.version AS version
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            JOIN taxonomy.version_skill AS skill ON skill.taxonomy_version_id = version.taxonomy_version_id
            JOIN taxonomy.proficiency AS proficiency
              ON proficiency.taxonomy_version_id = version.taxonomy_version_id
             AND proficiency.rank = CASE skill.skill_code WHEN 'sql' THEN 2 ELSE 2 END
            WHERE profile.code = 'data-steward'
              AND version.status = 'draft'
              AND skill.skill_code IN ('sql', 'modeling')
            ON CONFLICT (benchmark_version_id, skill_id) DO NOTHING
            """,
            connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnrichRequests(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE workforce.profile_correction AS correction
            SET title = CASE correction.report_employee_id
                    WHEN 'asha-rao' THEN 'Confirm the Data analyst profile'
                    WHEN 'dev-kapoor' THEN 'Modeling is short of the bar'
                    ELSE COALESCE(NULLIF(correction.title, ''), correction.note)
                END,
                evidence = CASE correction.report_employee_id
                    WHEN 'asha-rao' THEN 'Reporting-heavy work already on the analyst profile.'
                    WHEN 'dev-kapoor' THEN 'The sealed modeling rank is below the analyst bar.'
                    ELSE correction.evidence
                END,
                request_status = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM catalogue.profile_opening AS opening
                        WHERE opening.request_id = correction.request_id) THEN 'in-progress'
                    WHEN correction.report_employee_id = 'dev-kapoor' THEN 'needs-context'
                    ELSE 'new'
                END,
                competency_code = COALESCE(correction.competency_code, 'analysis')
            """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureNotes(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.overlap_note (note_id, title, detail)
            VALUES ($1, 'SQL and modeling both shape a dataset', 'The two definitions describe how a result is structured. Profiles should not count the same work twice.')
            ON CONFLICT (note_id) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue(OverlapId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO readiness.skill_alias (alias_id, skill_code, alias_name)
            VALUES ($1, 'sql', 'Structured query language')
            ON CONFLICT (skill_code, alias_name) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue(AliasId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO activation.cycle (zone_id, cycle_name, due_on, self_assessment, manager_validation, work_evidence)
            VALUES ($1, 'Evidence assessment', (timezone('utc', now()))::date + 30, true, true, true)
            ON CONFLICT (zone_id) DO NOTHING
            """,
            connection))
        {
            command.Parameters.AddWithValue(_zoneId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
