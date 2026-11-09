namespace SkillHorizon.Messaging;

public sealed record OutboxMessage(
    Guid Id,
    string ZoneId,
    string Type,
    string Payload,
    string CorrelationId,
    DateTimeOffset OccurredAt);

public sealed record OutboxDraft(string Type, string Payload, string CorrelationId);
