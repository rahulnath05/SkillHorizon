using Npgsql;

namespace SkillHorizon.Audit;

public sealed record AuditEntry(
    string SubjectId,
    string Action,
    string RecordType,
    Guid RecordId,
    string CorrelationId,
    string Detail);

public interface IAuditLog
{
    Task WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditEntry entry,
        CancellationToken cancellationToken);
}
