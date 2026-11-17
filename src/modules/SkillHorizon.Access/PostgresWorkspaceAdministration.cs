using System.Text.Json;
using Npgsql;
using SkillHorizon.Audit;

namespace SkillHorizon.Access;

public sealed class PostgresWorkspaceAdministration : IWorkspaceAdministration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;

    public PostgresWorkspaceAdministration(NpgsqlDataSource dataSource, IAuditLog audit)
    {
        _dataSource = dataSource;
        _audit = audit;
    }

    public async Task<IReadOnlyList<WorkspaceGrantRow>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT grant_id, subject_id, workspace, granted_at
            FROM access.workspace_grant
            ORDER BY subject_id, workspace
            """,
            connection);
        var grants = new List<WorkspaceGrantRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            grants.Add(new WorkspaceGrantRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));

        return grants;
    }

    public async Task<WorkspaceGrantRow> GrantAsync(
        string actorId,
        string subjectId,
        string workspace,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subject = RequireSubject(subjectId);
        var code = RequireWorkspace(workspace);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var existing = new NpgsqlCommand(
            """
            SELECT 1
            FROM access.workspace_grant
            WHERE subject_id = $1 AND workspace = $2
            """,
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue(subject);
            existing.Parameters.AddWithValue(code);
            if (await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("That workspace is already held.");
        }

        var grantId = Guid.NewGuid();
        DateTimeOffset grantedAt;
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
            VALUES ($1, $2, $3, now())
            RETURNING granted_at
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(grantId);
            insert.Parameters.AddWithValue(subject);
            insert.Parameters.AddWithValue(code);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            grantedAt = reader.GetFieldValue<DateTimeOffset>(0);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(
                actorId,
                "grant.issued",
                "access.workspace_grant",
                grantId,
                correlationId,
                JsonSerializer.Serialize(new { subjectId = subject, workspace = code }, Json)),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new WorkspaceGrantRow(grantId, subject, code, grantedAt);
    }

    public async Task RevokeAsync(
        string actorId,
        string subjectId,
        string workspace,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subject = RequireSubject(subjectId);
        var code = RequireWorkspace(workspace);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Guid grantId;
        await using (var existing = new NpgsqlCommand(
            """
            SELECT grant_id
            FROM access.workspace_grant
            WHERE subject_id = $1 AND workspace = $2
            """,
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue(subject);
            existing.Parameters.AddWithValue(code);
            var found = await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is not Guid id)
                throw new InvalidOperationException("That workspace is not held.");
            grantId = id;
        }

        await using (var delete = new NpgsqlCommand(
            """
            DELETE FROM access.workspace_grant
            WHERE grant_id = $1
            """,
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue(grantId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(
                actorId,
                "grant.revoked",
                "access.workspace_grant",
                grantId,
                correlationId,
                JsonSerializer.Serialize(new { subjectId = subject, workspace = code }, Json)),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string RequireSubject(string subjectId)
    {
        var subject = subjectId.Trim();
        if (subject.Length is 0 or > 80)
            throw new InvalidOperationException("A grant names a person.");
        return subject;
    }

    private static string RequireWorkspace(string workspace)
    {
        var code = workspace.Trim();
        if (!Workspaces.IsKnown(code))
            throw new InvalidOperationException("That workspace is not known.");
        return code;
    }
}
