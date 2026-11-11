using Npgsql;

namespace SkillHorizon.Api;

public sealed record PracticeRequestRow(
    Guid RequestId,
    string ReportEmployeeId,
    string DisplayName,
    string Note,
    DateTimeOffset RecordedAt,
    DateTimeOffset? AcceptedAt);

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

    private async Task<IReadOnlyList<PracticeRequestRow>> ReadAsync(
        string practiceCode,
        Guid? requestId,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT correction.request_id, correction.report_employee_id,
                   COALESCE(person.display_name, correction.report_employee_id),
                   correction.note, correction.recorded_at, acceptance.accepted_at
            FROM workforce.profile_correction AS correction
            LEFT JOIN workforce.person AS person ON person.employee_id = correction.report_employee_id
            LEFT JOIN attestation.request_acceptance AS acceptance ON acceptance.request_id = correction.request_id
            WHERE ($2::uuid IS NULL OR correction.request_id = $2)
              AND EXISTS (
                  SELECT 1
                  FROM attestation.cohort AS cohort
                  JOIN attestation.cohort_member AS member ON member.cohort_id = cohort.cohort_id
                  WHERE cohort.practice_code = $1
                    AND member.employee_id = correction.report_employee_id)
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
                reader.IsDBNull(5) ? null : Timestamp(reader.GetValue(5))));
        }

        return rows;
    }

    private static DateTimeOffset Timestamp(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A timestamp was not returned.")
    };
}
