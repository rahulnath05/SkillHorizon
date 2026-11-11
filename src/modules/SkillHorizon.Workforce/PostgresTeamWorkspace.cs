using Npgsql;

namespace SkillHorizon.Workforce;

public sealed class PostgresTeamWorkspace : ITeamWorkspace
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ITeamDirectory _directory;

    public PostgresTeamWorkspace(NpgsqlDataSource dataSource, ITeamDirectory directory)
    {
        _dataSource = dataSource;
        _directory = directory;
    }

    public async Task ProjectAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var managers = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT manager_employee_id FROM workforce.management_relationship
            UNION
            SELECT manager_employee_id FROM workforce.team_snapshot
            ORDER BY 1
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                managers.Add(reader.GetString(0));
        }

        foreach (var manager in managers)
            await Write(connection, manager, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TeamSnapshot?> ReadSnapshotAsync(string managerEmployeeId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset asOf;
        int reportCount;
        int attentionCount;
        await using (var command = new NpgsqlCommand(
            """
            SELECT as_of, report_count, attention_count
            FROM workforce.team_snapshot
            WHERE manager_employee_id = $1
            """,
            connection))
        {
            command.Parameters.AddWithValue(managerEmployeeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            asOf = Timestamp(reader.GetValue(0));
            reportCount = reader.GetInt32(1);
            attentionCount = reader.GetInt32(2);
        }

        var members = new List<TeamMember>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT employee_id, display_name, position, attention
            FROM workforce.team_member
            WHERE manager_employee_id = $1
            ORDER BY display_name, employee_id
            """,
            connection))
        {
            command.Parameters.AddWithValue(managerEmployeeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                members.Add(new TeamMember(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3)));
            }
        }

        return new TeamSnapshot(asOf, reportCount, attentionCount, members);
    }

    public async Task<TeamReviews> ReadReviewsAsync(string managerEmployeeId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var profiles = new List<ProfileReview>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT member.employee_id, member.display_name, profile.profile_id, profile.display_name
            FROM workforce.team_member AS member
            JOIN readiness.sealed_result AS result ON result.subject_id = member.employee_id
            JOIN benchmark.version AS sealed ON sealed.benchmark_version_id = result.benchmark_version_id
            JOIN benchmark.version AS published
                ON published.job_profile_id = sealed.job_profile_id
               AND published.status = 'published'
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = published.job_profile_id
            WHERE member.manager_employee_id = $1
              AND result.result_id = (
                  SELECT latest.result_id
                  FROM readiness.sealed_result AS latest
                  WHERE latest.subject_id = member.employee_id
                  ORDER BY latest.sealed_at DESC
                  LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM workforce.profile_confirmation AS confirmation
                  WHERE confirmation.manager_employee_id = $1
                    AND confirmation.report_employee_id = member.employee_id
                    AND confirmation.job_profile_id = profile.profile_id)
            ORDER BY member.display_name
            """,
            connection))
        {
            command.Parameters.AddWithValue(managerEmployeeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                profiles.Add(new ProfileReview(reader.GetString(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3)));
        }

        var assessments = new List<AssessmentReviewItem>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT member.employee_id, member.display_name, result.result_id
            FROM workforce.team_member AS member
            JOIN readiness.sealed_result AS result ON result.subject_id = member.employee_id
            WHERE member.manager_employee_id = $1
              AND result.result_id = (
                  SELECT latest.result_id
                  FROM readiness.sealed_result AS latest
                  WHERE latest.subject_id = member.employee_id
                  ORDER BY latest.sealed_at DESC
                  LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM workforce.assessment_review AS review
                  WHERE review.manager_employee_id = $1
                    AND review.result_id = result.result_id)
            ORDER BY member.display_name
            """,
            connection))
        {
            command.Parameters.AddWithValue(managerEmployeeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                assessments.Add(new AssessmentReviewItem(reader.GetString(0), reader.GetString(1), reader.GetGuid(2)));
        }

        return new TeamReviews(profiles, assessments);
    }

    public async Task ConfirmProfileAsync(string managerEmployeeId, string reportEmployeeId, CancellationToken cancellationToken)
    {
        await RequireReport(managerEmployeeId, reportEmployeeId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Guid profileId;
        await using (var command = new NpgsqlCommand(
            """
            SELECT published.job_profile_id
            FROM readiness.sealed_result AS result
            JOIN benchmark.version AS sealed ON sealed.benchmark_version_id = result.benchmark_version_id
            JOIN benchmark.version AS published
                ON published.job_profile_id = sealed.job_profile_id
               AND published.status = 'published'
            WHERE result.subject_id = $1
            ORDER BY result.sealed_at DESC
            LIMIT 1
            """,
            connection))
        {
            command.Parameters.AddWithValue(reportEmployeeId);
            var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is not Guid id)
                throw new InvalidOperationException("That person has no sealed result to confirm.");
            profileId = id;
        }

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO workforce.profile_confirmation (manager_employee_id, report_employee_id, job_profile_id, recorded_at)
            VALUES ($1, $2, $3, now())
            ON CONFLICT (manager_employee_id, report_employee_id, job_profile_id) DO NOTHING
            """,
            connection);
        insert.Parameters.AddWithValue(managerEmployeeId);
        insert.Parameters.AddWithValue(reportEmployeeId);
        insert.Parameters.AddWithValue(profileId);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReviewAssessmentAsync(string managerEmployeeId, string reportEmployeeId, string note, CancellationToken cancellationToken)
    {
        await RequireReport(managerEmployeeId, reportEmployeeId, cancellationToken).ConfigureAwait(false);
        if (note.Length > 500)
            throw new InvalidOperationException("An assessment note is at most 500 characters.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Guid resultId;
        await using (var command = new NpgsqlCommand(
            """
            SELECT result_id FROM readiness.sealed_result
            WHERE subject_id = $1
            ORDER BY sealed_at DESC
            LIMIT 1
            """,
            connection))
        {
            command.Parameters.AddWithValue(reportEmployeeId);
            var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is not Guid id)
                throw new InvalidOperationException("That person has no sealed result to review.");
            resultId = id;
        }

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO workforce.assessment_review (manager_employee_id, report_employee_id, result_id, note, recorded_at)
            VALUES ($1, $2, $3, $4, now())
            ON CONFLICT (manager_employee_id, result_id) DO NOTHING
            """,
            connection);
        insert.Parameters.AddWithValue(managerEmployeeId);
        insert.Parameters.AddWithValue(reportEmployeeId);
        insert.Parameters.AddWithValue(resultId);
        insert.Parameters.AddWithValue(note.Trim());
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> FileCorrectionAsync(string managerEmployeeId, string reportEmployeeId, string note, CancellationToken cancellationToken)
    {
        await RequireReport(managerEmployeeId, reportEmployeeId, cancellationToken).ConfigureAwait(false);
        var text = note.Trim();
        if (text.Length is < 1 or > 500)
            throw new InvalidOperationException("A profile correction needs a note of at most 500 characters.");
        var requestId = Guid.NewGuid();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO workforce.profile_correction (request_id, manager_employee_id, report_employee_id, note, recorded_at)
            VALUES ($1, $2, $3, $4, now())
            """,
            connection);
        command.Parameters.AddWithValue(requestId);
        command.Parameters.AddWithValue(managerEmployeeId);
        command.Parameters.AddWithValue(reportEmployeeId);
        command.Parameters.AddWithValue(text);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return requestId;
    }

    public async Task<IReadOnlyList<TeamSkillPerson>> ReadSkillsAsync(string managerEmployeeId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT member.employee_id, member.display_name, member.position,
                   target.skill_code, target.skill_name,
                   COALESCE(person.proficiency_rank, 0), COALESCE(person.proficiency_name, ''),
                   target.proficiency_rank, target.proficiency_name
            FROM workforce.team_member AS member
            LEFT JOIN readiness.sealed_result AS result
                ON result.subject_id = member.employee_id
               AND result.result_id = (
                   SELECT latest.result_id
                   FROM readiness.sealed_result AS latest
                   WHERE latest.subject_id = member.employee_id
                   ORDER BY latest.sealed_at DESC
                   LIMIT 1)
            LEFT JOIN benchmark.version AS sealed ON sealed.benchmark_version_id = result.benchmark_version_id
            LEFT JOIN benchmark.version AS published
                ON published.job_profile_id = sealed.job_profile_id
               AND published.status = 'published'
            LEFT JOIN benchmark.target AS target ON target.benchmark_version_id = published.benchmark_version_id
            LEFT JOIN readiness.proficiency AS person
                ON person.result_id = result.result_id
               AND person.skill_code = target.skill_code
            WHERE member.manager_employee_id = $1
            ORDER BY member.display_name, member.employee_id, target.skill_name
            """,
            connection);
        command.Parameters.AddWithValue(managerEmployeeId);
        var people = new List<TeamSkillPerson>();
        var skills = new List<TeamSkillMeasure>();
        string? employeeId = null;
        string displayName = "";
        string position = "";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var nextId = reader.GetString(0);
            if (employeeId != nextId)
            {
                if (employeeId is not null)
                    people.Add(new TeamSkillPerson(employeeId, displayName, position, skills.ToArray()));
                employeeId = nextId;
                displayName = reader.GetString(1);
                position = reader.GetString(2);
                skills = [];
            }

            if (!reader.IsDBNull(3))
            {
                skills.Add(new TeamSkillMeasure(
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetInt32(7),
                    reader.GetString(8)));
            }
        }

        if (employeeId is not null)
            people.Add(new TeamSkillPerson(employeeId, displayName, position, skills.ToArray()));
        return people;
    }

    public async Task<IReadOnlyList<CoachingCommitment>> ListCommitmentsAsync(string managerEmployeeId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT commitment.commitment_id, report.employee_id, report.display_name, commitment.title, commitment.due_on
            FROM workforce.coaching_commitment AS commitment
            JOIN workforce.management_relationship AS link
                ON link.manager_employee_id = commitment.manager_employee_id
               AND link.report_employee_id = commitment.report_employee_id
            JOIN workforce.person AS report ON report.employee_id = link.report_employee_id
            WHERE commitment.manager_employee_id = $1
              AND report.employment_status = 'active'
            ORDER BY commitment.due_on, report.display_name, commitment.title
            """,
            connection);
        command.Parameters.AddWithValue(managerEmployeeId);
        var commitments = new List<CoachingCommitment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            commitments.Add(new CoachingCommitment(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ReadDate(reader, 4)));
        }

        return commitments;
    }

    public async Task<CoachingCommitment> CreateCommitmentAsync(
        string managerEmployeeId,
        string reportEmployeeId,
        string title,
        DateOnly dueOn,
        CancellationToken cancellationToken)
    {
        await RequireReport(managerEmployeeId, reportEmployeeId, cancellationToken).ConfigureAwait(false);
        var text = title.Trim();
        if (text.Length is < 1 or > 200)
            throw new InvalidOperationException("A commitment needs a title of at most 200 characters.");
        var commitmentId = Guid.NewGuid();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO workforce.coaching_commitment (
                commitment_id, manager_employee_id, report_employee_id, title, due_on, recorded_at)
            VALUES ($1, $2, $3, $4, $5, now())
            """,
            connection);
        command.Parameters.AddWithValue(commitmentId);
        command.Parameters.AddWithValue(managerEmployeeId);
        command.Parameters.AddWithValue(reportEmployeeId);
        command.Parameters.AddWithValue(text);
        command.Parameters.AddWithValue(dueOn);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        string displayName;
        await using (var name = new NpgsqlCommand("SELECT display_name FROM workforce.person WHERE employee_id = $1", connection))
        {
            name.Parameters.AddWithValue(reportEmployeeId);
            displayName = (string)(await name.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        return new CoachingCommitment(commitmentId, reportEmployeeId, displayName, text, dueOn);
    }

    private async Task<ManagementLink> RequireReport(string managerEmployeeId, string reportEmployeeId, CancellationToken cancellationToken)
    {
        var link = await _directory.FindCurrentLinkAsync(managerEmployeeId, reportEmployeeId, cancellationToken).ConfigureAwait(false);
        return link ?? throw new UnauthorizedAccessException("That person is not an active direct report.");
    }

    private static async Task Write(NpgsqlConnection connection, string managerEmployeeId, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var members = new List<(string Id, string Name, string Position, bool Attention)>();
        await using (var command = new NpgsqlCommand(
            """
            WITH reports AS (
                SELECT report.employee_id, report.display_name
                FROM workforce.management_relationship AS link
                JOIN workforce.person AS report ON report.employee_id = link.report_employee_id
                JOIN workforce.person AS manager ON manager.employee_id = link.manager_employee_id
                WHERE link.manager_employee_id = $1
                  AND report.employment_status = 'active'
                  AND manager.employment_status = 'active'
            ),
            latest AS (
                SELECT DISTINCT ON (result.subject_id)
                    result.subject_id, result.result_id, result.benchmark_version_id
                FROM readiness.sealed_result AS result
                JOIN reports ON reports.employee_id = result.subject_id
                ORDER BY result.subject_id, result.sealed_at DESC
            ),
            gaps AS (
                SELECT DISTINCT latest.subject_id
                FROM latest
                JOIN benchmark.version AS sealed ON sealed.benchmark_version_id = latest.benchmark_version_id
                JOIN benchmark.version AS published
                    ON published.job_profile_id = sealed.job_profile_id
                   AND published.status = 'published'
                JOIN benchmark.target AS target ON target.benchmark_version_id = published.benchmark_version_id
                LEFT JOIN readiness.proficiency AS measured
                    ON measured.result_id = latest.result_id
                   AND measured.skill_code = target.skill_code
                WHERE COALESCE(measured.proficiency_rank, 0) < target.proficiency_rank
            )
            SELECT reports.employee_id, reports.display_name,
                   CASE
                       WHEN latest.subject_id IS NULL THEN 'not-measured'
                       WHEN gaps.subject_id IS NOT NULL THEN 'gap'
                       ELSE 'met'
                   END,
                   gaps.subject_id IS NOT NULL
            FROM reports
            LEFT JOIN latest ON latest.subject_id = reports.employee_id
            LEFT JOIN gaps ON gaps.subject_id = reports.employee_id
            ORDER BY reports.display_name, reports.employee_id
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(managerEmployeeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                members.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        }

        await using (var upsert = new NpgsqlCommand(
            """
            INSERT INTO workforce.team_snapshot (manager_employee_id, as_of, report_count, attention_count)
            VALUES ($1, now(), $2, $3)
            ON CONFLICT (manager_employee_id) DO UPDATE
                SET as_of = EXCLUDED.as_of,
                    report_count = EXCLUDED.report_count,
                    attention_count = EXCLUDED.attention_count
            """,
            connection,
            transaction))
        {
            upsert.Parameters.AddWithValue(managerEmployeeId);
            upsert.Parameters.AddWithValue(members.Count);
            upsert.Parameters.AddWithValue(members.Count(member => member.Attention));
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var clear = new NpgsqlCommand(
            "DELETE FROM workforce.team_member WHERE manager_employee_id = $1",
            connection,
            transaction))
        {
            clear.Parameters.AddWithValue(managerEmployeeId);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var member in members)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO workforce.team_member (manager_employee_id, employee_id, display_name, position, attention)
                VALUES ($1, $2, $3, $4, $5)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue(managerEmployeeId);
            insert.Parameters.AddWithValue(member.Id);
            insert.Parameters.AddWithValue(member.Name);
            insert.Parameters.AddWithValue(member.Position);
            insert.Parameters.AddWithValue(member.Attention);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset Timestamp(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A timestamp was not returned.")
    };

    private static DateOnly ReadDate(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime time => DateOnly.FromDateTime(time),
            _ => throw new InvalidOperationException("A commitment date could not be read.")
        };
    }
}
