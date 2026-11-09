using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SkillHorizon.Hosting;

namespace SkillHorizon.Messaging;

public sealed class PostgresOutbox : IOutbox
{
    private readonly SkillHorizonOptions _options;

    public PostgresOutbox(IOptions<SkillHorizonOptions> options)
    {
        _options = options.Value;
    }

    public async Task EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OutboxDraft draft,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO messaging.outbox (
                outbox_id, zone_id, message_type, payload, correlation_id, occurred_at
            )
            VALUES ($1, $2, $3, $4, $5, $6)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(_options.ZoneId);
        command.Parameters.AddWithValue(draft.Type);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
            Value = draft.Payload
        });
        command.Parameters.AddWithValue(draft.CorrelationId);
        command.Parameters.AddWithValue(DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PostgresOutboxPublisher : IOutboxPublisher
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMessageBus _bus;
    private readonly ILogger<PostgresOutboxPublisher> _logger;

    public PostgresOutboxPublisher(NpgsqlDataSource dataSource, IMessageBus bus, ILogger<PostgresOutboxPublisher> logger)
    {
        _dataSource = dataSource;
        _bus = bus;
        _logger = logger;
    }

    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var messages = new List<OutboxMessage>();

        await using (var select = new NpgsqlCommand(
            """
            SELECT outbox_id, zone_id, message_type, payload, correlation_id, occurred_at
            FROM messaging.outbox
            WHERE published_at IS NULL
            ORDER BY occurred_at
            FOR UPDATE SKIP LOCKED
            LIMIT 20
            """,
            connection,
            transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(new OutboxMessage(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        foreach (var message in messages)
        {
            await _bus.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            await using var update = new NpgsqlCommand(
                "UPDATE messaging.outbox SET published_at = $2 WHERE outbox_id = $1",
                connection,
                transaction);
            update.Parameters.AddWithValue(message.Id);
            update.Parameters.AddWithValue(DateTimeOffset.UtcNow);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (messages.Count > 0)
        {
            _logger.LogInformation("Outbox published {Count} message(s).", messages.Count);
        }

        return messages.Count;
    }
}
