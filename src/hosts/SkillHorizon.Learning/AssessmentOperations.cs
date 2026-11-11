using System.Net.Http.Headers;
using Npgsql;

namespace SkillHorizon.Learning;

public sealed record ProgrammeSnapshot(
    Guid SnapshotId,
    string Kind,
    Guid? WaveId,
    DateTimeOffset AsOf,
    int CompletedCount,
    int OpenCount,
    int DurationSeconds);

public sealed class AssessmentOperations
{
    private readonly NpgsqlDataSource _assessment;
    private readonly HttpClient _core;

    public AssessmentOperations(NpgsqlDataSource assessment, HttpClient core)
    {
        _assessment = assessment;
        _core = core;
    }

    public async Task<IReadOnlyList<ProgrammeSnapshot>> TakeProgrammeAsync(
        string bearer,
        string workspace,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        var counted = await CountAsync(null, cancellationToken).ConfigureAwait(false);
        var operations = await StoreAsync("operations", null, counted, cancellationToken).ConfigureAwait(false);
        var scorecard = await StoreAsync("scorecard", null, counted, cancellationToken).ConfigureAwait(false);
        return [operations, scorecard];
    }

    public async Task<ProgrammeSnapshot> TakePilotAsync(
        string bearer,
        string workspace,
        string correlationId,
        Guid waveId,
        CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        if (waveId == Guid.Empty)
            throw new InvalidOperationException("A pilot report names a wave.");
        var counted = await CountAsync(waveId, cancellationToken).ConfigureAwait(false);
        return await StoreAsync("pilot", waveId, counted, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProgrammeSnapshot?> LatestAsync(
        string bearer,
        string workspace,
        string correlationId,
        string kind,
        Guid? waveId,
        CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        if (kind is not ("operations" or "scorecard" or "pilot"))
            throw new InvalidOperationException("That snapshot is not part of the studio.");
        if (kind == "pilot" && waveId is null)
            throw new InvalidOperationException("A pilot report names a wave.");

        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT snapshot_id, kind, wave_id, as_of, completed_count, open_count, duration_seconds
            FROM assessment.programme_snapshot
            WHERE kind = $1 AND wave_id IS NOT DISTINCT FROM $2
            ORDER BY as_of DESC
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue(kind);
        command.Parameters.Add(new NpgsqlParameter { Value = waveId.HasValue ? waveId.Value : DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return Read(reader);
    }

    private async Task<(int Completed, int Open, int Duration)> CountAsync(Guid? waveId, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                count(*) FILTER (WHERE status = 'sealed'),
                count(*) FILTER (WHERE status = 'open'),
                coalesce(sum(GREATEST(0, EXTRACT(EPOCH FROM (sealed_at - opened_at)))) FILTER (WHERE status = 'sealed' AND sealed_at IS NOT NULL), 0)::integer
            FROM assessment.sitting
            WHERE $1::uuid IS NULL OR wave_id = $1
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter { Value = waveId.HasValue ? waveId.Value : DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return (Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)), Convert.ToInt32(reader.GetValue(2)));
    }

    private async Task<ProgrammeSnapshot> StoreAsync(string kind, Guid? waveId, (int Completed, int Open, int Duration) counted, CancellationToken cancellationToken)
    {
        var snapshotId = Guid.NewGuid();
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO assessment.programme_snapshot (
                snapshot_id, kind, wave_id, as_of, completed_count, open_count, duration_seconds)
            VALUES ($1, $2, $3, now(), $4, $5, $6)
            RETURNING as_of
            """,
            connection);
        command.Parameters.AddWithValue(snapshotId);
        command.Parameters.AddWithValue(kind);
        command.Parameters.Add(new NpgsqlParameter { Value = waveId.HasValue ? waveId.Value : DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        command.Parameters.AddWithValue(counted.Completed);
        command.Parameters.AddWithValue(counted.Open);
        command.Parameters.AddWithValue(counted.Duration);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var asOf = reader.GetFieldValue<DateTimeOffset>(0);
        return new ProgrammeSnapshot(snapshotId, kind, waveId, asOf, counted.Completed, counted.Open, counted.Duration);
    }

    private static ProgrammeSnapshot Read(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6));

    private async Task RequireAuthor(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "studio/author");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("X-Active-Workspace", workspace);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        using var response = await _core.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("This command is refused for the active workspace.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The core API refused the assessment studio.");
    }
}
