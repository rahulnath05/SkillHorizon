using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SkillHorizon.Messaging;

public sealed class RedisMessageBus : IMessageBus
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisMessageBus> _logger;

    public RedisMessageBus(IConnectionMultiplexer redis, ILogger<RedisMessageBus> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var database = _redis.GetDatabase();
        var body = JsonSerializer.Serialize(message);
        await database.ListLeftPushAsync($"skillhorizon:{message.ZoneId}:outbox", body).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Published outbox message. zone={Zone} correlation_id={CorrelationId} outbox_id={OutboxId} type={MessageType}",
            message.ZoneId,
            message.CorrelationId,
            message.Id,
            message.Type);
    }
}
