using Npgsql;

namespace SkillHorizon.Readiness;

public sealed class PostgresPracticeProjection : IPracticeProjection
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IProgressCensus _progress;

    public PostgresPracticeProjection(NpgsqlDataSource dataSource, IProgressCensus progress)
    {
        _dataSource = dataSource;
        _progress = progress;
    }

    public async Task MarkDirtyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE readiness.snapshot SET dirty = true WHERE subject_kind = 'practice'",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ProjectAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var practices = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT practice_code FROM attestation.pack
            UNION
            SELECT practice_code FROM attestation.cohort
            UNION
            SELECT subject_key FROM readiness.snapshot WHERE subject_kind = 'practice'
            ORDER BY 1
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                practices.Add(reader.GetString(0));
        }

        foreach (var practice in practices)
        {
            await Write(connection, practice, cancellationToken).ConfigureAwait(false);
            await new PostgresPracticePicture().WriteAsync(connection, practice, _progress, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<PracticeSnapshot?> ReadAsync(string practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT subject_key, as_of, dirty, member_count, measured_count, gap_count, cohort_count
            FROM readiness.snapshot
            WHERE subject_kind = 'practice' AND subject_key = $1
            """,
            connection);
        command.Parameters.AddWithValue(practiceCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new PracticeSnapshot(
            reader.GetString(0),
            Timestamp(reader.GetValue(1)),
            reader.GetBoolean(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6));
    }

    private static async Task Write(NpgsqlConnection connection, string practiceCode, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int members;
        int measured;
        int gaps;
        int cohorts;
        await using (var command = new NpgsqlCommand(
            """
            WITH members AS (
                SELECT DISTINCT member.employee_id, cohort.job_profile_id
                FROM attestation.cohort AS cohort
                JOIN attestation.cohort_member AS member ON member.cohort_id = cohort.cohort_id
                JOIN workforce.person AS person ON person.employee_id = member.employee_id
                WHERE cohort.practice_code = $1
                  AND person.employment_status = 'active'
            ),
            latest AS (
                SELECT DISTINCT ON (result.subject_id) result.subject_id, result.result_id
                FROM readiness.sealed_result AS result
                JOIN members ON members.employee_id = result.subject_id
                ORDER BY result.subject_id, result.sealed_at DESC
            ),
            gaps AS (
                SELECT DISTINCT members.employee_id
                FROM members
                JOIN latest ON latest.subject_id = members.employee_id
                JOIN benchmark.version AS published
                    ON published.job_profile_id = members.job_profile_id
                   AND published.status = 'published'
                JOIN benchmark.target AS target ON target.benchmark_version_id = published.benchmark_version_id
                LEFT JOIN readiness.proficiency AS measured
                    ON measured.result_id = latest.result_id
                   AND measured.skill_code = target.skill_code
                WHERE COALESCE(measured.proficiency_rank, 0) < target.proficiency_rank
            )
            SELECT
                (SELECT count(DISTINCT employee_id) FROM members),
                (SELECT count(*) FROM latest),
                (SELECT count(*) FROM gaps),
                (SELECT count(*) FROM attestation.cohort WHERE practice_code = $1)
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(practiceCode);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            members = Convert.ToInt32(reader.GetValue(0));
            measured = Convert.ToInt32(reader.GetValue(1));
            gaps = Convert.ToInt32(reader.GetValue(2));
            cohorts = Convert.ToInt32(reader.GetValue(3));
        }

        await using var upsert = new NpgsqlCommand(
            """
            INSERT INTO readiness.snapshot (
                snapshot_id, subject_kind, subject_key, as_of, dirty,
                member_count, measured_count, gap_count, cohort_count)
            VALUES ($1, 'practice', $2, now(), false, $3, $4, $5, $6)
            ON CONFLICT (subject_kind, subject_key) DO UPDATE
                SET as_of = EXCLUDED.as_of,
                    dirty = false,
                    member_count = EXCLUDED.member_count,
                    measured_count = EXCLUDED.measured_count,
                    gap_count = EXCLUDED.gap_count,
                    cohort_count = EXCLUDED.cohort_count
            """,
            connection,
            transaction);
        upsert.Parameters.AddWithValue(Guid.NewGuid());
        upsert.Parameters.AddWithValue(practiceCode);
        upsert.Parameters.AddWithValue(members);
        upsert.Parameters.AddWithValue(measured);
        upsert.Parameters.AddWithValue(gaps);
        upsert.Parameters.AddWithValue(cohorts);
        await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset Timestamp(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A timestamp was not returned.")
    };
}
