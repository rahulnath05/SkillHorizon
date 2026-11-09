using Npgsql;

namespace SkillHorizon.Access;

public sealed class PostgresGrantCheck : IGrantCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresGrantCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<bool> HasGrantAsync(string subjectId, string workspace, CancellationToken cancellationToken)
    {
        if (!Workspaces.IsKnown(workspace))
            return false;

        await using var command = _dataSource.CreateCommand(
            """
            SELECT 1
            FROM access.workspace_grant
            WHERE subject_id = $1 AND workspace = $2
            """);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(workspace);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is not null;
    }

    public async Task<IReadOnlyList<string>> ListWorkspacesAsync(string subjectId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT workspace
            FROM access.workspace_grant
            WHERE subject_id = $1
            ORDER BY workspace
            """);
        command.Parameters.AddWithValue(subjectId);
        var workspaces = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var workspace = reader.GetString(0);
            if (Workspaces.IsKnown(workspace))
                workspaces.Add(workspace);
        }

        return workspaces;
    }
}
