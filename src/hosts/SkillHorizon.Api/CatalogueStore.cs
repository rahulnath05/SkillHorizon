using Npgsql;
using SkillHorizon.JobArchitecture;

namespace SkillHorizon.Api;

public sealed record ProfileRequestRow(
    Guid RequestId,
    string ManagerEmployeeId,
    string ReportEmployeeId,
    string DisplayName,
    string Note,
    DateTimeOffset RecordedAt,
    Guid? ProfileId,
    string? ProfileName);

public sealed record SentDraftRow(
    Guid BenchmarkVersionId,
    Guid JobProfileId,
    string ProfileName,
    int VersionNumber,
    DateTimeOffset SentAt);

public sealed record PracticeSnapshotRow(
    string PracticeCode,
    DateTimeOffset AsOf,
    int MemberCount,
    int MeasuredCount,
    int GapCount,
    int CohortCount);

public sealed class CatalogueStore
{
    private readonly NpgsqlDataSource _dataSource;

    public CatalogueStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<ProfileRequestRow>> ListRequestsAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT correction.request_id, correction.manager_employee_id, correction.report_employee_id,
                   COALESCE(person.display_name, correction.report_employee_id),
                   correction.note, correction.recorded_at,
                   opening.profile_id, profile.display_name
            FROM workforce.profile_correction AS correction
            LEFT JOIN workforce.person AS person ON person.employee_id = correction.report_employee_id
            LEFT JOIN catalogue.profile_opening AS opening ON opening.request_id = correction.request_id
            LEFT JOIN job_architecture.job_profile AS profile ON profile.profile_id = opening.profile_id
            ORDER BY correction.recorded_at DESC, correction.request_id
            """);
        var rows = new List<ProfileRequestRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ProfileRequestRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                Timestamp(reader.GetValue(5)),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows;
    }

    public async Task<FrameworkProfile> OpenAsync(
        Guid requestId,
        string frameworkCode,
        string levelCode,
        string profileName,
        CancellationToken cancellationToken)
    {
        var framework = frameworkCode.Trim();
        var level = levelCode.Trim();
        var name = profileName.Trim();
        if (framework.Length == 0 || level.Length == 0 || name.Length is 0 or > 200)
            throw new InvalidOperationException("A draft profile needs a framework, a level, and a name.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await RequestExists(connection, transaction, requestId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("That profile request does not exist.");

        var existing = await FindOpening(connection, transaction, requestId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        var levelRow = await FindLevel(connection, transaction, framework, level, cancellationToken).ConfigureAwait(false);
        var profileId = Guid.NewGuid();
        var code = ProfileCode(name, requestId);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO job_architecture.job_profile (profile_id, framework_id, level_id, code, display_name)
            VALUES ($1, $2, $3, $4, $5)
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(profileId);
            insert.Parameters.AddWithValue(levelRow.FrameworkId);
            insert.Parameters.AddWithValue(levelRow.LevelId);
            insert.Parameters.AddWithValue(code);
            insert.Parameters.AddWithValue(name);
            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new InvalidOperationException("That profile code is already in use.");
            }
        }

        await using (var opening = new NpgsqlCommand(
            """
            INSERT INTO catalogue.profile_opening (request_id, profile_id, opened_at)
            VALUES ($1, $2, now())
            """,
            connection,
            transaction))
        {
            opening.Parameters.AddWithValue(requestId);
            opening.Parameters.AddWithValue(profileId);
            await opening.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new FrameworkProfile(profileId, code, name, level, levelRow.LevelName, levelRow.LevelRank);
    }

    public async Task<IReadOnlyList<SentDraftRow>> ListSentAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT version.benchmark_version_id, version.job_profile_id, profile.display_name,
                   version.version_number, submission.sent_at
            FROM catalogue.submission AS submission
            JOIN benchmark.version AS version ON version.benchmark_version_id = submission.benchmark_version_id
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            WHERE version.status = 'draft'
            ORDER BY submission.sent_at, profile.display_name
            """);
        var rows = new List<SentDraftRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new SentDraftRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt32(3),
                Timestamp(reader.GetValue(4))));
        }

        return rows;
    }

    public async Task<SentDraftRow> SendAsync(Guid benchmarkVersionId, string sentBy, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string? status = null;
        await using (var locked = new NpgsqlCommand(
            """
            SELECT status
            FROM benchmark.version
            WHERE benchmark_version_id = $1
            FOR UPDATE
            """,
            connection,
            transaction))
        {
            locked.Parameters.AddWithValue(benchmarkVersionId);
            var found = await locked.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is string value)
                status = value;
        }

        if (status is null)
            throw new InvalidOperationException("That draft does not exist.");
        if (status != "draft")
            throw new InvalidOperationException("Only a benchmark draft can be sent.");

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO catalogue.submission (benchmark_version_id, sent_by, sent_at)
            VALUES ($1, $2, now())
            ON CONFLICT (benchmark_version_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(benchmarkVersionId);
            insert.Parameters.AddWithValue(sentBy);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        SentDraftRow? row = null;
        await using (var read = new NpgsqlCommand(
            """
            SELECT version.benchmark_version_id, version.job_profile_id, profile.display_name,
                   version.version_number, submission.sent_at
            FROM catalogue.submission AS submission
            JOIN benchmark.version AS version ON version.benchmark_version_id = submission.benchmark_version_id
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            WHERE submission.benchmark_version_id = $1
            """,
            connection,
            transaction))
        {
            read.Parameters.AddWithValue(benchmarkVersionId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                row = new SentDraftRow(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    Timestamp(reader.GetValue(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return row ?? throw new InvalidOperationException("That draft was not sent.");
    }

    public async Task<(IReadOnlyList<PracticeSnapshotRow> Snapshots, int RequestCount, int ApprovalCount)> ReadOverviewAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<PracticeSnapshotRow>();
        await using (var command = _dataSource.CreateCommand(
            """
            SELECT subject_key, as_of, member_count, measured_count, gap_count, cohort_count
            FROM readiness.snapshot
            WHERE subject_kind = 'practice'
            ORDER BY subject_key
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                snapshots.Add(new PracticeSnapshotRow(
                    reader.GetString(0),
                    Timestamp(reader.GetValue(1)),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)));
            }
        }

        var requests = await Count(
            """
            SELECT count(*)
            FROM workforce.profile_correction AS correction
            WHERE NOT EXISTS (
                SELECT 1 FROM catalogue.profile_opening AS opening WHERE opening.request_id = correction.request_id)
            """,
            cancellationToken).ConfigureAwait(false);
        var approvals = await Count(
            """
            SELECT count(*)
            FROM catalogue.submission AS submission
            JOIN benchmark.version AS version ON version.benchmark_version_id = submission.benchmark_version_id
            WHERE version.status = 'draft'
            """,
            cancellationToken).ConfigureAwait(false);
        return (snapshots, requests, approvals);
    }

    private static async Task<bool> RequestExists(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM workforce.profile_correction WHERE request_id = $1",
            connection,
            transaction);
        command.Parameters.AddWithValue(requestId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<FrameworkProfile?> FindOpening(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT profile.profile_id, profile.code, profile.display_name, level.code, level.display_name, level.rank
            FROM catalogue.profile_opening AS opening
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = opening.profile_id
            JOIN job_architecture.level AS level ON level.level_id = profile.level_id
            WHERE opening.request_id = $1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new FrameworkProfile(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5));
    }

    private static async Task<LevelRow> FindLevel(NpgsqlConnection connection, NpgsqlTransaction transaction, string frameworkCode, string levelCode, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT framework.framework_id, level.level_id, level.display_name, level.rank
            FROM job_architecture.framework AS framework
            JOIN job_architecture.level AS level ON level.framework_id = framework.framework_id
            WHERE framework.code = $1 AND level.code = $2
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(frameworkCode);
        command.Parameters.AddWithValue(levelCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("That framework level does not exist.");
        return new LevelRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3));
    }

    private async Task<int> Count(string sql, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(sql);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static string ProfileCode(string name, Guid requestId)
    {
        var slug = new string(name.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        if (slug.Length == 0)
            slug = "profile";
        if (slug.Length > 40)
            slug = slug[..40].Trim('-');
        return slug + "-" + requestId.ToString("N")[..8];
    }

    private static DateTimeOffset Timestamp(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A catalogue row has no timestamp.")
    };

    private sealed record LevelRow(Guid FrameworkId, Guid LevelId, string LevelName, int LevelRank);
}
