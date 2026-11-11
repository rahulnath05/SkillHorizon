using System.Text.Json;
using Npgsql;
using SkillHorizon.Audit;
using SkillHorizon.Messaging;

namespace SkillHorizon.Benchmark;

public sealed class PostgresMarketDemand : IMarketDemand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;
    private readonly IOutbox _outbox;

    public PostgresMarketDemand(NpgsqlDataSource dataSource, IAuditLog audit, IOutbox outbox)
    {
        _dataSource = dataSource;
        _audit = audit;
        _outbox = outbox;
    }

    public async Task<MarketDraftView?> ReadDraftAsync(string practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT practice_code, note, posting_count, role_count, status, updated_at
            FROM market.demand
            WHERE practice_code = $1
            """,
            connection);
        command.Parameters.AddWithValue(practiceCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new MarketDraftView(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            Timestamp(reader.GetValue(5)));
    }

    public async Task<MarketPublishedView?> ReadPublishedAsync(string practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT practice_code, note, posting_count, role_count, published_at
            FROM reference.market_demand
            WHERE practice_code = $1
            """,
            connection);
        command.Parameters.AddWithValue(practiceCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new MarketPublishedView(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            Timestamp(reader.GetValue(4)));
    }

    public async Task<MarketDraftView> SaveDraftAsync(string practiceCode, string note, int postingCount, int roleCount, CancellationToken cancellationToken)
    {
        var practice = practiceCode.Trim();
        var text = note.Trim();
        if (practice.Length == 0 || text.Length is < 1 or > 500)
            throw new InvalidOperationException("A market note is one to 500 characters for a practice.");
        if (postingCount < 0 || roleCount < 0)
            throw new InvalidOperationException("Market counts cannot be negative.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await RequirePractice(connection, transaction, practice, cancellationToken).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO market.demand (practice_code, note, posting_count, role_count, status, updated_at)
            VALUES ($1, $2, $3, $4, 'draft', now())
            ON CONFLICT (practice_code) DO UPDATE
                SET note = EXCLUDED.note,
                    posting_count = EXCLUDED.posting_count,
                    role_count = EXCLUDED.role_count,
                    status = 'draft',
                    updated_at = now()
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(practice);
            command.Parameters.AddWithValue(text);
            command.Parameters.AddWithValue(postingCount);
            command.Parameters.AddWithValue(roleCount);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadDraftAsync(practice, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<MarketPublication> PublishAsync(string practiceCode, string subjectId, string correlationId, CancellationToken cancellationToken)
    {
        var practice = practiceCode.Trim();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string note;
        int postingCount;
        int roleCount;
        DateTimeOffset publishedAt;
        await using (var command = new NpgsqlCommand(
            """
            UPDATE market.demand
            SET status = 'published', updated_at = now()
            WHERE practice_code = $1
            RETURNING note, posting_count, role_count, updated_at
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(practice);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Save a market demand note before publishing it.");
            note = reader.GetString(0);
            postingCount = reader.GetInt32(1);
            roleCount = reader.GetInt32(2);
            publishedAt = Timestamp(reader.GetValue(3));
        }

        var publication = new MarketPublication(practice, note, postingCount, roleCount, publishedAt);
        var payload = JsonSerializer.Serialize(publication, Json);
        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(subjectId, BenchmarkModule.MarketPublishedMessage, "market.demand", Guid.NewGuid(), correlationId, payload),
            cancellationToken).ConfigureAwait(false);
        await _outbox.EnqueueAsync(
            connection,
            transaction,
            new OutboxDraft(BenchmarkModule.MarketPublishedMessage, payload, correlationId),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return publication;
    }

    private static async Task RequirePractice(NpgsqlConnection connection, NpgsqlTransaction transaction, string practiceCode, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM taxonomy.practice WHERE code = $1",
            connection,
            transaction);
        command.Parameters.AddWithValue(practiceCode);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException("That practice is not in the taxonomy.");
    }

    private static DateTimeOffset Timestamp(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A timestamp was not returned.")
    };
}
