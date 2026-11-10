using Npgsql;

namespace SkillHorizon.Audit;

public sealed class PostgresAuditLog : IAuditLog
{
    public async Task WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_record (
                audit_id, subject_id, action, record_type, record_id, correlation_id, detail, occurred_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, now())
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(entry.SubjectId);
        command.Parameters.AddWithValue(entry.Action);
        command.Parameters.AddWithValue(entry.RecordType);
        command.Parameters.AddWithValue(entry.RecordId);
        command.Parameters.AddWithValue(entry.CorrelationId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
            Value = entry.Detail
        });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
