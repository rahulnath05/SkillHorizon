using Npgsql;

namespace SkillHorizon.Api;

public sealed record RequestSkill(string Name, string Level);

public sealed record PracticeRequestRow(
    Guid RequestId,
    string ReportEmployeeId,
    string DisplayName,
    string Note,
    DateTimeOffset RecordedAt,
    DateTimeOffset? AcceptedAt,
    string Title,
    string Evidence,
    string RequestStatus,
    string CompetencyName,
    string? ProfileName,
    string ManagerEmployeeId,
    IReadOnlyList<RequestSkill> Skills);

public sealed class PracticeReviewStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PracticeReviewStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public Task<IReadOnlyList<PracticeRequestRow>> ListAsync(string practiceCode, CancellationToken cancellationToken) =>
        ReadAsync(practiceCode, null, cancellationToken);

    public async Task<PracticeRequestRow> AcceptAsync(
        string subjectId,
        string practiceCode,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var practice = practiceCode.Trim();
        if (practice.Length == 0)
            throw new InvalidOperationException("A practice is required.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var inPractice = false;
        await using (var found = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM workforce.profile_correction AS correction
                WHERE correction.request_id = $1
                  AND EXISTS (
                      SELECT 1
                      FROM attestation.cohort AS cohort
                      JOIN attestation.cohort_member AS member ON member.cohort_id = cohort.cohort_id
                      WHERE cohort.practice_code = $2
                        AND member.employee_id = correction.report_employee_id))
            """,
            connection,
            transaction))
        {
            found.Parameters.AddWithValue(requestId);
            found.Parameters.AddWithValue(practice);
            inPractice = await found.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
        }

        if (!inPractice)
        {
            await using var exists = new NpgsqlCommand(
                "SELECT 1 FROM workforce.profile_correction WHERE request_id = $1",
                connection,
                transaction);
            exists.Parameters.AddWithValue(requestId);
            if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidOperationException("That profile request does not exist.");
            throw new InvalidOperationException("That request is not in this practice.");
        }

        await using (var opened = new NpgsqlCommand(
            "SELECT 1 FROM catalogue.profile_opening WHERE request_id = $1",
            connection,
            transaction))
        {
            opened.Parameters.AddWithValue(requestId);
            if (await opened.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("A benchmark is already in progress for this request.");
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO attestation.request_acceptance (request_id, practice_code, accepted_by, accepted_at)
            VALUES ($1, $2, $3, now())
            ON CONFLICT (request_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(requestId);
            insert.Parameters.AddWithValue(practice);
            insert.Parameters.AddWithValue(subjectId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var rows = await ReadAsync(practice, requestId, cancellationToken).ConfigureAwait(false);
        return rows.Count == 1
            ? rows[0]
            : throw new InvalidOperationException("That profile request does not exist.");
    }

    public async Task<PracticeRequestRow> FileAsync(
        string subjectId,
        string practiceCode,
        string profileName,
        string purpose,
        string path,
        string? competencyCode,
        IReadOnlyList<(string Name, string Rank)> anchors,
        CancellationToken cancellationToken)
    {
        var practice = practiceCode.Trim();
        var title = profileName.Trim();
        if (practice.Length == 0)
            throw new InvalidOperationException("A practice is required.");
        if (title.Length is 0 or > 200)
            throw new InvalidOperationException("Name the profile before sending the request.");

        var note = purpose.Trim();
        if (note.Length == 0)
            note = path == "ld"
                ? "L&D authors this profile from the brief."
                : "Competency lead draft for L&D to complete.";
        if (note.Length > 500)
            throw new InvalidOperationException("The market purpose is longer than 500 characters.");

        var evidence = path == "ld"
            ? "Requested for L&D to author from scratch."
            : anchors.Count == 0
                ? "Competency lead draft with no anchors set yet."
                : string.Join("; ", anchors.Select(anchor =>
                    string.IsNullOrWhiteSpace(anchor.Rank) ? anchor.Name : $"{anchor.Name} L{anchor.Rank}"));
        if (evidence.Length > 500)
            evidence = evidence[..500];

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var competency = await CompetencyForPractice(connection, practice, competencyCode, cancellationToken).ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO workforce.profile_correction (
                request_id, manager_employee_id, report_employee_id, note, recorded_at,
                title, evidence, request_status, competency_code)
            VALUES ($1, $2, $2, $3, now(), $4, $5, 'new', $6)
            """,
            connection))
        {
            insert.Parameters.AddWithValue(requestId);
            insert.Parameters.AddWithValue(subjectId);
            insert.Parameters.AddWithValue(note);
            insert.Parameters.AddWithValue(title);
            insert.Parameters.AddWithValue(evidence);
            insert.Parameters.AddWithValue(competency);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var rows = await ReadAsync(practice, requestId, cancellationToken).ConfigureAwait(false);
        return rows.Count == 1
            ? rows[0]
            : throw new InvalidOperationException("That profile request could not be read back.");
    }

    private static async Task<string> CompetencyForPractice(
        NpgsqlConnection connection,
        string practice,
        string? requested,
        CancellationToken cancellationToken)
    {
        var code = requested?.Trim() ?? "";
        await using var command = new NpgsqlCommand(
            """
            SELECT competency.code
            FROM taxonomy.competency AS competency
            JOIN taxonomy.practice AS practice ON practice.practice_id = competency.practice_id
            WHERE practice.code = $1
              AND ($2 = '' OR competency.code = $2)
            ORDER BY CASE WHEN competency.code = $2 THEN 0 ELSE 1 END, competency.code
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue(practice);
        command.Parameters.AddWithValue(code);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is string competency
            ? competency
            : throw new InvalidOperationException("That practice has no competency to attach the request to.");
    }

    private async Task<IReadOnlyList<PracticeRequestRow>> ReadAsync(
        string practiceCode,
        Guid? requestId,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT correction.request_id, correction.report_employee_id,
                   COALESCE(person.display_name, correction.report_employee_id),
                   correction.note, correction.recorded_at, acceptance.accepted_at,
                   COALESCE(NULLIF(correction.title, ''), correction.note),
                   correction.evidence, correction.request_status,
                   COALESCE(competency.display_name, ''),
                   profile.display_name,
                   correction.manager_employee_id
            FROM workforce.profile_correction AS correction
            LEFT JOIN workforce.person AS person ON person.employee_id = correction.report_employee_id
            LEFT JOIN attestation.request_acceptance AS acceptance ON acceptance.request_id = correction.request_id
            LEFT JOIN taxonomy.competency AS competency ON competency.code = correction.competency_code
            LEFT JOIN catalogue.profile_opening AS opening ON opening.request_id = correction.request_id
            LEFT JOIN job_architecture.job_profile AS profile ON profile.profile_id = opening.profile_id
            WHERE ($2::uuid IS NULL OR correction.request_id = $2)
              AND (
                  EXISTS (
                      SELECT 1
                      FROM attestation.cohort AS cohort
                      JOIN attestation.cohort_member AS member ON member.cohort_id = cohort.cohort_id
                      WHERE cohort.practice_code = $1
                        AND member.employee_id = correction.report_employee_id)
                  OR EXISTS (
                      SELECT 1
                      FROM taxonomy.practice AS practice
                      WHERE practice.practice_id = competency.practice_id
                        AND practice.code = $1))
            ORDER BY correction.recorded_at DESC, correction.request_id
            """);
        command.Parameters.AddWithValue(practiceCode);
        command.Parameters.AddWithValue(requestId is Guid id ? id : DBNull.Value);
        var rows = new List<PracticeRequestRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PracticeRequestRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Timestamp(reader.GetValue(4)),
                reader.IsDBNull(5) ? null : Timestamp(reader.GetValue(5)),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetString(11),
                []));
        }

        return await WithSkills(rows, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PracticeRequestRow>> WithSkills(
        List<PracticeRequestRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return rows;
        var skills = new Dictionary<Guid, List<RequestSkill>>();
        await using var command = _dataSource.CreateCommand(
            """
            SELECT opening.request_id, target.skill_name, target.proficiency_rank, target.level_set
            FROM catalogue.profile_opening AS opening
            JOIN benchmark.version AS version ON version.job_profile_id = opening.profile_id
            JOIN benchmark.target AS target ON target.benchmark_version_id = version.benchmark_version_id
            WHERE opening.request_id = ANY($1)
              AND version.benchmark_version_id = (
                  SELECT picked.benchmark_version_id
                  FROM benchmark.version AS picked
                  WHERE picked.job_profile_id = opening.profile_id
                  ORDER BY CASE WHEN picked.status = 'draft' THEN 0 ELSE 1 END, picked.version_number DESC
                  LIMIT 1)
            ORDER BY target.skill_name
            """);
        command.Parameters.AddWithValue(rows.Select(row => row.RequestId).ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var requestId = reader.GetGuid(0);
            if (!skills.TryGetValue(requestId, out var list))
            {
                list = [];
                skills[requestId] = list;
            }

            var levelSet = reader.GetBoolean(3);
            var rank = reader.IsDBNull(2) ? "" : reader.GetInt32(2).ToString();
            list.Add(new RequestSkill(reader.GetString(1), levelSet && rank.Length > 0 ? "L" + rank : "Set later"));
        }

        return rows.Select(row => skills.TryGetValue(row.RequestId, out var list)
            ? row with { Skills = list }
            : row).ToList();
    }

    private static DateTimeOffset Timestamp(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A timestamp was not returned.")
    };
}
