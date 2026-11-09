using Npgsql;

namespace SkillHorizon.Messaging;

public interface IOutbox
{
    Task EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OutboxDraft draft,
        CancellationToken cancellationToken);
}

public interface IOutboxPublisher
{
    Task<int> PublishPendingAsync(CancellationToken cancellationToken);
}

public interface IMessageBus
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
