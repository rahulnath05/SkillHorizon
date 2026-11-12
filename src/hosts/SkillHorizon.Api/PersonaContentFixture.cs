using Npgsql;
using SkillHorizon.Benchmark;
using SkillHorizon.Readiness;
using SkillHorizon.Workforce;

namespace SkillHorizon.Api;

public sealed class PersonaContentFixture
{
    public static readonly Guid AshaSittingId = Guid.Parse("a11a0002-0000-4000-8000-000000000002");

    private const string MarketNote =
        "Hiring for the data practice is concentrated in SQL analysis and data modeling. The associate analyst bar is the one most postings still ask for.";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IWorkforceSync _sync;
    private readonly IMarketDemand _market;
    private readonly ITeamWorkspace _teams;
    private readonly IPracticeProjection _practice;

    public PersonaContentFixture(
        NpgsqlDataSource dataSource,
        IWorkforceSync sync,
        IMarketDemand market,
        ITeamWorkspace teams,
        IPracticeProjection practice)
    {
        _dataSource = dataSource;
        _sync = sync;
        _market = market;
        _teams = teams;
        _practice = practice;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await EnsurePeople(cancellationToken).ConfigureAwait(false);
        var wave = await LatestWaveAsync(cancellationToken).ConfigureAwait(false);
        if (wave is null)
            return;

        await AdmitAsync(wave.Value.WaveId, "asha-rao", cancellationToken).ConfigureAwait(false);
        await AdmitAsync(wave.Value.WaveId, "dev-kapoor", cancellationToken).ConfigureAwait(false);
        await SetPopulationAsync(wave.Value.WaveId, cancellationToken).ConfigureAwait(false);

        var targets = await TargetsAsync(wave.Value.BenchmarkVersionId, cancellationToken).ConfigureAwait(false);
        var scale = await ScaleAsync(wave.Value.BenchmarkVersionId, cancellationToken).ConfigureAwait(false);
        if (targets.Count == 0 || scale.Count == 0)
            return;

        await SealAsync("person-single", Id("101"), Id("111"), wave.Value, targets, scale, "sql", null, cancellationToken).ConfigureAwait(false);
        await SealAsync("asha-rao", Id("102"), Id("112"), wave.Value, targets, scale, "sql", null, cancellationToken).ConfigureAwait(false);
        await SealAsync("dev-kapoor", Id("103"), Id("113"), wave.Value, targets, scale, null, "modeling", cancellationToken).ConfigureAwait(false);

        await EnsurePacksAsync(cancellationToken).ConfigureAwait(false);
        await EnsureCohortAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRequestsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureCommitmentsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDeclarationsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSubmissionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureMarketAsync(cancellationToken).ConfigureAwait(false);
        await EnsureAuditAsync(wave.Value.WaveId, cancellationToken).ConfigureAwait(false);
        await EnsureMalpracticeAsync(cancellationToken).ConfigureAwait(false);

        await _teams.ProjectAsync(cancellationToken).ConfigureAwait(false);
        await _practice.ProjectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePeople(CancellationToken cancellationToken)
    {
        try
        {
            await _sync.SyncAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }

        if (!await ExistsAsync("SELECT 1 FROM workforce.person WHERE employee_id = 'asha-rao'", cancellationToken).ConfigureAwait(false))
            await InsertFeedAsync(cancellationToken).ConfigureAwait(false);

        if (!await ExistsAsync(
                "SELECT 1 FROM workforce.management_relationship WHERE manager_employee_id = 'person-multi' AND report_employee_id = 'asha-rao'",
                cancellationToken).ConfigureAwait(false))
            await RebuildRelationshipsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertFeedAsync(CancellationToken cancellationToken)
    {
        var feed = await new FixtureHrFeed().ReadAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in feed)
        {
            var employeeId = record.EmployeeId?.Trim() ?? "";
            var legalEntityId = record.LegalEntityId?.Trim() ?? "";
            var countryCode = record.CountryCode?.Trim() ?? "";
            var status = record.EmploymentStatus?.Trim().ToLowerInvariant() switch
            {
                "active" => "active",
                "leaver" => "closed",
                _ => null
            };
            if (status is null || employeeId.Length == 0 || legalEntityId.Length == 0 || countryCode.Length == 0)
                continue;

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO workforce.person (
                    employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                    personal_learning_assigned, display_name, closed_at, projected_at, joined_at)
                VALUES (
                    $1, $2, $3, $4, $5, $6, $7,
                    CASE WHEN $5 = 'closed' THEN now() ELSE NULL END,
                    now(), now())
                ON CONFLICT (employee_id) DO UPDATE SET
                    legal_entity_id = EXCLUDED.legal_entity_id,
                    country_code = EXCLUDED.country_code,
                    manager_employee_id = EXCLUDED.manager_employee_id,
                    employment_status = EXCLUDED.employment_status,
                    personal_learning_assigned = EXCLUDED.personal_learning_assigned,
                    display_name = EXCLUDED.display_name,
                    closed_at = CASE
                        WHEN EXCLUDED.employment_status = 'active' THEN NULL
                        ELSE COALESCE(workforce.person.closed_at, EXCLUDED.closed_at)
                    END,
                    projected_at = now()
                """,
                connection);
            command.Parameters.AddWithValue(employeeId);
            command.Parameters.AddWithValue(legalEntityId);
            command.Parameters.AddWithValue(countryCode);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = string.IsNullOrWhiteSpace(record.ManagerEmployeeId) ? DBNull.Value : record.ManagerEmployeeId.Trim() });
            command.Parameters.AddWithValue(status);
            command.Parameters.AddWithValue(record.PersonalLearningAssigned);
            command.Parameters.AddWithValue(string.IsNullOrWhiteSpace(record.DisplayName) ? employeeId : record.DisplayName.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (status == "active" && record.PersonalLearningAssigned)
            {
                await using var grant = new NpgsqlCommand(
                    """
                    INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
                    VALUES (gen_random_uuid(), $1, 'employee', now())
                    ON CONFLICT (subject_id, workspace) DO NOTHING
                    """,
                    connection);
                grant.Parameters.AddWithValue(employeeId);
                await grant.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RebuildRelationshipsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM workforce.management_relationship;

            INSERT INTO workforce.management_relationship (manager_employee_id, report_employee_id)
            SELECT manager_employee_id, employee_id
            FROM workforce.person
            WHERE manager_employee_id IS NOT NULL
              AND btrim(manager_employee_id) <> ''
              AND manager_employee_id <> employee_id
            """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Guid WaveId, Guid BenchmarkVersionId)?> LatestWaveAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT wave.wave_id, wave.benchmark_version_id
            FROM activation.admission AS admission
            JOIN activation.wave AS wave ON wave.wave_id = admission.wave_id
            WHERE admission.subject_id = 'person-single'
            ORDER BY wave.created_at DESC
            LIMIT 1
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return (reader.GetGuid(0), reader.GetGuid(1));
    }

    private async Task AdmitAsync(Guid waveId, string subjectId, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync("SELECT 1 FROM workforce.person WHERE employee_id = $1 AND employment_status = 'active'", cancellationToken, subjectId).ConfigureAwait(false))
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO activation.admission (wave_id, subject_id, admitted_at, route)
            VALUES ($1, $2, now(), 'rolling')
            ON CONFLICT (wave_id, subject_id) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(waveId);
        command.Parameters.AddWithValue(subjectId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetPopulationAsync(Guid waveId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE activation.wave
            SET population = (SELECT count(*) FROM activation.admission WHERE wave_id = $1)
            WHERE wave_id = $1
            """,
            connection);
        command.Parameters.AddWithValue(waveId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<TargetRow>> TargetsAsync(Guid benchmarkVersionId, CancellationToken cancellationToken)
    {
        var targets = new List<TargetRow>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT skill_code, skill_name, proficiency_rank
            FROM benchmark.target
            WHERE benchmark_version_id = $1
            ORDER BY skill_name
            """,
            connection);
        command.Parameters.AddWithValue(benchmarkVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            targets.Add(new TargetRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return targets;
    }

    private async Task<Dictionary<int, ScaleRow>> ScaleAsync(Guid benchmarkVersionId, CancellationToken cancellationToken)
    {
        var scale = new Dictionary<int, ScaleRow>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT proficiency.rank, proficiency.code, proficiency.display_name
            FROM benchmark.version AS version
            JOIN taxonomy.proficiency AS proficiency ON proficiency.taxonomy_version_id = version.taxonomy_version_id
            WHERE version.benchmark_version_id = $1
            """,
            connection);
        command.Parameters.AddWithValue(benchmarkVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            scale[reader.GetInt32(0)] = new ScaleRow(reader.GetString(1), reader.GetString(2));
        return scale;
    }

    private async Task SealAsync(
        string subjectId,
        Guid resultId,
        Guid authorizationId,
        (Guid WaveId, Guid BenchmarkVersionId) wave,
        IReadOnlyList<TargetRow> targets,
        IReadOnlyDictionary<int, ScaleRow> scale,
        string? gapSkill,
        string? omitSkill,
        CancellationToken cancellationToken)
    {
        if (!await ExistsAsync("SELECT 1 FROM workforce.person WHERE employee_id = $1 AND employment_status = 'active'", cancellationToken, subjectId).ConfigureAwait(false))
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var authorization = new NpgsqlCommand(
            """
            INSERT INTO activation.sitting_authorization (
                authorization_id, token_hash, subject_id, wave_id, benchmark_version_id, created_at, expires_at, consumed_at)
            VALUES ($1, $2, $3, $4, $5, now(), now() + interval '2 hours', now())
            ON CONFLICT (authorization_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            authorization.Parameters.AddWithValue(authorizationId);
            authorization.Parameters.AddWithValue("persona-auth-" + subjectId);
            authorization.Parameters.AddWithValue(subjectId);
            authorization.Parameters.AddWithValue(wave.WaveId);
            authorization.Parameters.AddWithValue(wave.BenchmarkVersionId);
            await authorization.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO readiness.sealed_result (
                result_id, authorization_id, subject_id, benchmark_version_id, wave_id, sealed_at)
            VALUES ($1, $2, $3, $4, $5, now())
            ON CONFLICT (result_id) DO NOTHING
            RETURNING result_id
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue(resultId);
        insert.Parameters.AddWithValue(authorizationId);
        insert.Parameters.AddWithValue(subjectId);
        insert.Parameters.AddWithValue(wave.BenchmarkVersionId);
        insert.Parameters.AddWithValue(wave.WaveId);
        var inserted = await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is Guid;
        if (inserted)
        {
            foreach (var target in targets)
            {
                if (string.Equals(target.Code, omitSkill, StringComparison.Ordinal))
                    continue;
                var rank = string.Equals(target.Code, gapSkill, StringComparison.Ordinal) && target.Rank > 1 ? target.Rank - 1 : target.Rank;
                if (!scale.TryGetValue(rank, out var level))
                    continue;
                await using var proficiency = new NpgsqlCommand(
                    """
                    INSERT INTO readiness.proficiency (
                        result_id, skill_code, skill_name, proficiency_rank, proficiency_code, proficiency_name)
                    VALUES ($1, $2, $3, $4, $5, $6)
                    ON CONFLICT (result_id, skill_code) DO NOTHING
                    """,
                    connection,
                    transaction);
                proficiency.Parameters.AddWithValue(resultId);
                proficiency.Parameters.AddWithValue(target.Code);
                proficiency.Parameters.AddWithValue(target.Name);
                proficiency.Parameters.AddWithValue(rank);
                proficiency.Parameters.AddWithValue(level.Code);
                proficiency.Parameters.AddWithValue(level.Name);
                await proficiency.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePacksAsync(CancellationToken cancellationToken)
    {
        var analyst = await ProfileAsync("data-analyst", cancellationToken).ConfigureAwait(false);
        var consultant = await ProfileAsync("data-consultant", cancellationToken).ConfigureAwait(false);
        if (analyst is null || consultant is null)
            return;

        var analystBenchmark = await PublishedBenchmarkAsync(analyst.ProfileId, cancellationToken).ConfigureAwait(false);
        var consultantBenchmark = await PublishedBenchmarkAsync(consultant.ProfileId, cancellationToken).ConfigureAwait(false);
        var taxonomyId = await PublishedTaxonomyAsync(cancellationToken).ConfigureAwait(false);
        if (analystBenchmark is null || consultantBenchmark is null || taxonomyId is null)
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sealedPack = Id("201");
        var submittedPack = Id("202");
        if (await InsertPackAsync(connection, transaction, sealedPack, "sealed", "now() - interval '7 days'", cancellationToken).ConfigureAwait(false))
        {
            var aligned = Id("211");
            var exception = Id("212");
            await InsertRowAsync(connection, transaction, aligned, sealedPack, analyst, "aligned", null, cancellationToken).ConfigureAwait(false);
            await InsertRowAsync(connection, transaction, exception, sealedPack, consultant, "tightening", "The consultant bar tightens SQL from intermediate to advanced.", cancellationToken).ConfigureAwait(false);
            await using (var review = new NpgsqlCommand(
                """
                INSERT INTO attestation.row_review (row_id, reviewed_by, reviewed_at)
                VALUES ($1, 'person-lead', now() - interval '6 days')
                ON CONFLICT (row_id) DO NOTHING
                """,
                connection,
                transaction))
            {
                review.Parameters.AddWithValue(aligned);
                await review.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var decision = new NpgsqlCommand(
                """
                INSERT INTO attestation.exception_decision (row_id, decision, decided_by, decided_at)
                VALUES ($1, $2, 'person-lead', now() - interval '6 days')
                ON CONFLICT (row_id) DO NOTHING
                """,
                connection,
                transaction))
            {
                decision.Parameters.AddWithValue(exception);
                decision.Parameters.AddWithValue("Accepted for the data practice baseline.");
                await decision.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var sealId = Id("231");
            await using (var seal = new NpgsqlCommand(
                """
                INSERT INTO attestation.seal (seal_id, pack_id, taxonomy_version_id, lead_subject_id, sealed_at)
                VALUES ($1, $2, $3, 'person-lead', now() - interval '6 days')
                ON CONFLICT (seal_id) DO NOTHING
                """,
                connection,
                transaction))
            {
                seal.Parameters.AddWithValue(sealId);
                seal.Parameters.AddWithValue(sealedPack);
                seal.Parameters.AddWithValue(taxonomyId.Value);
                await seal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await LinkBenchmarkAsync(connection, transaction, sealId, analyst.ProfileId, analystBenchmark.Value, cancellationToken).ConfigureAwait(false);
            await LinkBenchmarkAsync(connection, transaction, sealId, consultant.ProfileId, consultantBenchmark.Value, cancellationToken).ConfigureAwait(false);
            await using (var baseline = new NpgsqlCommand(
                """
                INSERT INTO attestation.baseline (practice_code, pack_id, taxonomy_version_id, sealed_at)
                VALUES ('data', $1, $2, now() - interval '6 days')
                ON CONFLICT (practice_code) DO NOTHING
                """,
                connection,
                transaction))
            {
                baseline.Parameters.AddWithValue(sealedPack);
                baseline.Parameters.AddWithValue(taxonomyId.Value);
                await baseline.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (await InsertPackAsync(connection, transaction, submittedPack, "submitted", "now()", cancellationToken).ConfigureAwait(false))
        {
            await InsertRowAsync(connection, transaction, Id("221"), submittedPack, analyst, "aligned", null, cancellationToken).ConfigureAwait(false);
            await InsertRowAsync(connection, transaction, Id("222"), submittedPack, consultant, "new_bar", "A new bar for the consultant profile is in review.", cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> InsertPackAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid packId,
        string status,
        string submittedAtSql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO attestation.pack (pack_id, practice_code, status, submitted_by, submitted_at)
            VALUES ($1, 'data', $2, 'person-ld', {submittedAtSql})
            ON CONFLICT (pack_id) DO NOTHING
            RETURNING pack_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(packId);
        command.Parameters.AddWithValue(status);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is Guid;
    }

    private static async Task InsertRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid rowId,
        Guid packId,
        ProfileRow profile,
        string classification,
        string? note,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO attestation.pack_row (
                row_id, pack_id, job_profile_id, profile_code, profile_name, classification, exception_note)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (row_id) DO NOTHING
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(rowId);
        command.Parameters.AddWithValue(packId);
        command.Parameters.AddWithValue(profile.ProfileId);
        command.Parameters.AddWithValue(profile.Code);
        command.Parameters.AddWithValue(profile.Name);
        command.Parameters.AddWithValue(classification);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)note ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task LinkBenchmarkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sealId,
        Guid profileId,
        Guid benchmarkVersionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO attestation.sealed_benchmark (seal_id, job_profile_id, benchmark_version_id)
            VALUES ($1, $2, $3)
            ON CONFLICT (seal_id, job_profile_id) DO NOTHING
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(sealId);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(benchmarkVersionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCohortAsync(CancellationToken cancellationToken)
    {
        var profileId = await ScalarGuidAsync(
            "SELECT profile_id FROM job_architecture.job_profile WHERE code = 'data-analyst'",
            cancellationToken).ConfigureAwait(false);
        if (profileId is null)
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var cohortId = Id("241");
        await using (var cohort = new NpgsqlCommand(
            """
            INSERT INTO attestation.cohort (
                cohort_id, practice_code, country_code, org_unit, job_profile_id, lead_subject_id, created_at)
            VALUES ($1, 'data', 'IN', NULL, $2, 'person-lead', now())
            ON CONFLICT (cohort_id) DO NOTHING
            """,
            connection))
        {
            cohort.Parameters.AddWithValue(cohortId);
            cohort.Parameters.AddWithValue(profileId.Value);
            await cohort.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var employeeId in new[] { "person-single", "asha-rao", "dev-kapoor" })
        {
            await using var member = new NpgsqlCommand(
                """
                INSERT INTO attestation.cohort_member (cohort_id, employee_id)
                VALUES ($1, $2)
                ON CONFLICT (cohort_id, employee_id) DO NOTHING
                """,
                connection);
            member.Parameters.AddWithValue(cohortId);
            member.Parameters.AddWithValue(employeeId);
            await member.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureRequestsAsync(CancellationToken cancellationToken)
    {
        await InsertRequestAsync(Id("301"), "asha-rao", "The Data analyst profile fits, with a reporting-heavy context to confirm.", cancellationToken).ConfigureAwait(false);
        await InsertRequestAsync(Id("302"), "dev-kapoor", "Modeling is short of the bar. Please confirm the analyst profile still holds.", cancellationToken).ConfigureAwait(false);
        var profileId = await ScalarGuidAsync(
            "SELECT profile_id FROM job_architecture.job_profile WHERE code = 'data-analyst'",
            cancellationToken).ConfigureAwait(false);
        if (profileId is null)
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var opening = new NpgsqlCommand(
            """
            INSERT INTO catalogue.profile_opening (request_id, profile_id, opened_at)
            VALUES ($1, $2, now())
            ON CONFLICT (request_id) DO NOTHING
            """,
            connection);
        opening.Parameters.AddWithValue(Id("301"));
        opening.Parameters.AddWithValue(profileId.Value);
        await opening.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertRequestAsync(Guid requestId, string reportId, string note, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync("SELECT 1 FROM workforce.person WHERE employee_id = $1", cancellationToken, reportId).ConfigureAwait(false))
            return;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO workforce.profile_correction (
                request_id, manager_employee_id, report_employee_id, note, recorded_at)
            VALUES ($1, 'person-multi', $2, $3, now())
            ON CONFLICT (request_id) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(requestId);
        command.Parameters.AddWithValue(reportId);
        command.Parameters.AddWithValue(note);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCommitmentsAsync(CancellationToken cancellationToken)
    {
        await InsertCommitmentAsync(Id("311"), "asha-rao", "Review the SQL analysis note before the next check-in", 21, cancellationToken).ConfigureAwait(false);
        await InsertCommitmentAsync(Id("312"), "dev-kapoor", "Work a modeling example against the analyst bar", 14, cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertCommitmentAsync(Guid commitmentId, string reportId, string title, int days, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(
                "SELECT 1 FROM workforce.management_relationship WHERE manager_employee_id = 'person-multi' AND report_employee_id = $1",
                cancellationToken,
                reportId).ConfigureAwait(false))
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO workforce.coaching_commitment (
                commitment_id, manager_employee_id, report_employee_id, title, due_on, recorded_at)
            VALUES ($1, 'person-multi', $2, $3, (timezone('utc', now()))::date + $4, now())
            ON CONFLICT (commitment_id) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(commitmentId);
        command.Parameters.AddWithValue(reportId);
        command.Parameters.AddWithValue(title);
        command.Parameters.AddWithValue(days);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDeclarationsAsync(CancellationToken cancellationToken)
    {
        await InsertDeclarationAsync("person-single", "sql", 2, "Intermediate", cancellationToken).ConfigureAwait(false);
        await InsertDeclarationAsync("asha-rao", "modeling", 2, "Intermediate", cancellationToken).ConfigureAwait(false);
        await InsertDeclarationAsync("dev-kapoor", "sql", 2, "Intermediate", cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertDeclarationAsync(string subjectId, string skillCode, int rank, string name, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync("SELECT 1 FROM workforce.person WHERE employee_id = $1", cancellationToken, subjectId).ConfigureAwait(false))
            return;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO readiness.declaration (
                declaration_id, subject_id, skill_code, declared_rank, declared_name, declared_at)
            VALUES (gen_random_uuid(), $1, $2, $3, $4, now())
            ON CONFLICT (subject_id, skill_code) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(skillCode);
        command.Parameters.AddWithValue(rank);
        command.Parameters.AddWithValue(name);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSubmissionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO catalogue.submission (benchmark_version_id, sent_by, sent_at)
            SELECT version.benchmark_version_id, 'person-ld', now()
            FROM benchmark.version AS version
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            WHERE profile.code = 'data-analyst' AND version.status = 'draft'
            ON CONFLICT (benchmark_version_id) DO NOTHING
            """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureMarketAsync(CancellationToken cancellationToken)
    {
        var current = await _market.ReadDraftAsync("data", cancellationToken).ConfigureAwait(false);
        if (current is null)
            current = await _market.SaveDraftAsync("data", MarketNote, 36, 8, cancellationToken).ConfigureAwait(false);
        if (current.Status != "published")
        {
            await _market.PublishAsync("data", "person-ld", "persona-content", cancellationToken).ConfigureAwait(false);
            current = await _market.ReadDraftAsync("data", cancellationToken).ConfigureAwait(false);
        }

        if (current is null || current.Status != "published")
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO reference.market_demand (practice_code, note, posting_count, role_count, published_at)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (practice_code) DO UPDATE SET
                note = EXCLUDED.note,
                posting_count = EXCLUDED.posting_count,
                role_count = EXCLUDED.role_count,
                published_at = EXCLUDED.published_at
            """,
            connection);
        command.Parameters.AddWithValue(current.PracticeCode);
        command.Parameters.AddWithValue(current.Note);
        command.Parameters.AddWithValue(current.PostingCount);
        command.Parameters.AddWithValue(current.RoleCount);
        command.Parameters.AddWithValue(current.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAuditAsync(Guid waveId, CancellationToken cancellationToken)
    {
        await InsertAuditAsync(Id("401"), "person-ld", "wave.rolling", "activation.wave", waveId, "{\"admitted\":2}", cancellationToken).ConfigureAwait(false);
        await InsertAuditAsync(Id("402"), "person-lead", "pack.submitted", "attestation.pack", Id("202"), "{\"practice\":\"data\",\"rows\":2}", cancellationToken).ConfigureAwait(false);
        await InsertAuditAsync(Id("403"), "person-single", "result.sealed", "readiness.sealed_result", Id("101"), "{\"skills\":2}", cancellationToken).ConfigureAwait(false);
        await InsertAuditAsync(Id("404"), "person-admin", "malpractice.opened", "audit.malpractice_case", Id("501"), "{\"status\":\"open\"}", cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertAuditAsync(
        Guid auditId,
        string subjectId,
        string action,
        string recordType,
        Guid recordId,
        string detail,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_record (
                audit_id, subject_id, action, record_type, record_id, correlation_id, detail, occurred_at)
            VALUES ($1, $2, $3, $4, $5, 'persona-content', $6::jsonb, now())
            ON CONFLICT (audit_id) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(auditId);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(recordType);
        command.Parameters.AddWithValue(recordId);
        command.Parameters.AddWithValue(detail);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureMalpracticeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.malpractice_case (case_id, sitting_id, status, opened_by, opened_at)
            VALUES ($1, $2, 'open', 'person-admin', now())
            ON CONFLICT (case_id) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(Id("501"));
        command.Parameters.AddWithValue(AshaSittingId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProfileRow?> ProfileAsync(string code, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT profile_id, code, display_name FROM job_architecture.job_profile WHERE code = $1",
            connection);
        command.Parameters.AddWithValue(code);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new ProfileRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    }

    private async Task<Guid?> PublishedBenchmarkAsync(Guid profileId, CancellationToken cancellationToken) =>
        await ScalarGuidAsync(
            "SELECT benchmark_version_id FROM benchmark.version WHERE job_profile_id = $1 AND status = 'published'",
            cancellationToken,
            profileId).ConfigureAwait(false);

    private async Task<Guid?> PublishedTaxonomyAsync(CancellationToken cancellationToken) =>
        await ScalarGuidAsync(
            "SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'published'",
            cancellationToken).ConfigureAwait(false);

    private async Task<Guid?> ScalarGuidAsync(string sql, CancellationToken cancellationToken, object? parameter = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameter is not null)
            command.Parameters.AddWithValue(parameter);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is Guid id ? id : null;
    }

    private async Task<bool> ExistsAsync(string sql, CancellationToken cancellationToken, string? parameter = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameter is not null)
            command.Parameters.AddWithValue(parameter);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static Guid Id(string suffix) => Guid.Parse("a11a0001-0000-4000-8000-000000000" + suffix);

    private sealed record TargetRow(string Code, string Name, int Rank);

    private sealed record ScaleRow(string Code, string Name);

    private sealed record ProfileRow(Guid ProfileId, string Code, string Name);
}
