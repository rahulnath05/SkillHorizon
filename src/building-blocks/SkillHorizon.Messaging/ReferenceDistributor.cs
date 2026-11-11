using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using SkillHorizon.Hosting;
using StackExchange.Redis;

namespace SkillHorizon.Messaging;

public sealed record ReferenceDestination(string ZoneId, string QueueUrl);

public sealed class ReferenceDistributor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IConnectionMultiplexer _local;
    private readonly SkillHorizonOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, IConnectionMultiplexer> _remote = new(StringComparer.Ordinal);

    public ReferenceDistributor(
        NpgsqlDataSource dataSource,
        IConnectionMultiplexer local,
        IOptions<SkillHorizonOptions> options)
    {
        _dataSource = dataSource;
        _local = local;
        _options = options.Value;
    }

    public async Task<int> DeliverAsync(IReadOnlyList<ReferenceDestination> subscribers, CancellationToken cancellationToken)
    {
        var delivered = 0;
        foreach (var subscriber in subscribers)
        {
            if (string.IsNullOrWhiteSpace(subscriber.ZoneId))
                continue;

            delivered += await DeliverToAsync(subscriber, cancellationToken).ConfigureAwait(false);
        }

        return delivered;
    }

    public Task<int> ReceiveAsync(
        Func<Guid, string, string, CancellationToken, Task> apply,
        CancellationToken cancellationToken) =>
        ReceiveAsync(_options.ZoneId, apply, cancellationToken);

    public async Task<int> ReceiveAsync(
        string zoneId,
        Func<Guid, string, string, CancellationToken, Task> apply,
        CancellationToken cancellationToken)
    {
        var database = _local.GetDatabase();
        var key = Inbox(zoneId);
        var received = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var value = await database.ListRightPopAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
                break;

            var message = JsonSerializer.Deserialize<OutboxMessage>(value.ToString())
                ?? throw new InvalidOperationException("A reference message could not be read.");
            await apply(message.Id, message.Type, message.Payload, cancellationToken).ConfigureAwait(false);
            received++;
        }

        return received;
    }

    private async Task<int> DeliverToAsync(ReferenceDestination subscriber, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var pending = new List<OutboxMessage>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT outbox.outbox_id, outbox.zone_id, outbox.message_type, outbox.payload::text, outbox.correlation_id, outbox.occurred_at
            FROM messaging.outbox AS outbox
            WHERE outbox.message_type IN ('taxonomy.published', 'benchmark.published')
              AND outbox.zone_id <> $1
              AND NOT EXISTS (
                  SELECT 1
                  FROM messaging.reference_delivery AS delivery
                  WHERE delivery.outbox_id = outbox.outbox_id
                    AND delivery.subscriber_zone_id = $1)
            ORDER BY outbox.occurred_at
            FOR UPDATE OF outbox
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(subscriber.ZoneId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                pending.Add(new OutboxMessage(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    Timestamp(reader.GetValue(5))));
            }
        }

        if (pending.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var database = Database(subscriber);
        foreach (var message in pending)
        {
            var body = JsonSerializer.Serialize(message);
            await database.ListLeftPushAsync(Inbox(subscriber.ZoneId), body).WaitAsync(cancellationToken).ConfigureAwait(false);
            await using var mark = new NpgsqlCommand(
                """
                INSERT INTO messaging.reference_delivery (outbox_id, subscriber_zone_id, delivered_at)
                VALUES ($1, $2, now())
                """,
                connection,
                transaction);
            mark.Parameters.AddWithValue(message.Id);
            mark.Parameters.AddWithValue(subscriber.ZoneId);
            await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return pending.Count;
    }

    private IDatabase Database(ReferenceDestination subscriber)
    {
        if (string.IsNullOrWhiteSpace(subscriber.QueueUrl))
            return _local.GetDatabase();

        lock (_gate)
        {
            if (_remote.TryGetValue(subscriber.QueueUrl, out var existing))
                return existing.GetDatabase();

            var configuration = ConfigurationOptions.Parse(subscriber.QueueUrl);
            configuration.AbortOnConnectFail = true;
            var connection = ConnectionMultiplexer.Connect(configuration);
            _remote[subscriber.QueueUrl] = connection;
            return connection.GetDatabase();
        }
    }

    private static string Inbox(string zoneId) => "skillhorizon:" + zoneId + ":reference";

    private static DateTimeOffset Timestamp(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A reference message has no timestamp.")
    };
}
