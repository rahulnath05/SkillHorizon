using Npgsql;
using NpgsqlTypes;

namespace SkillHorizon.Readiness;

public sealed class EmptyProgressCensus : IProgressCensus
{
    public Task<ProgressCounts> CountAsync(IReadOnlyCollection<string> subjectIds, CancellationToken cancellationToken) =>
        Task.FromResult(new ProgressCounts(0, 0, 0));
}

public sealed class PostgresProgressCensus : IProgressCensus, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProgressCensus(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<ProgressCounts> CountAsync(IReadOnlyCollection<string> subjectIds, CancellationToken cancellationToken)
    {
        var ids = subjectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return new ProgressCounts(0, 0, 0);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM progress.path WHERE subject_id = ANY($1)),
                (SELECT count(*) FROM progress.step WHERE subject_id = ANY($1)),
                (SELECT count(*) FROM progress.step WHERE subject_id = ANY($1) AND status = 'assigned')
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = ids });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new ProgressCounts(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)),
            Convert.ToInt32(reader.GetValue(2)));
    }

    public void Dispose() => _dataSource.Dispose();
}
