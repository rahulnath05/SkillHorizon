using Npgsql;

namespace SkillHorizon.JobArchitecture;

public sealed class PostgresJobArchitecture : IJobArchitecture
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresJobArchitecture(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task ImportAsync(IReadOnlyList<FrameworkImport> frameworks, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var framework in frameworks)
        {
            var frameworkId = await UpsertFramework(connection, transaction, framework, cancellationToken).ConfigureAwait(false);
            var levels = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var level in framework.Levels.OrderBy(level => level.Rank))
            {
                var levelId = await UpsertLevel(connection, transaction, frameworkId, level, cancellationToken).ConfigureAwait(false);
                levels[level.Code.Trim()] = levelId;
            }

            foreach (var profile in framework.Profiles)
            {
                if (!levels.TryGetValue(profile.LevelCode.Trim(), out var levelId))
                    throw new InvalidOperationException($"Level '{profile.LevelCode}' is not in framework '{framework.Code}'.");
                await UpsertProfile(connection, transaction, frameworkId, levelId, profile, cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FrameworkView>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var frameworks = new List<FrameworkView>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT framework_id, code, display_name
            FROM job_architecture.framework
            ORDER BY display_name, code
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                frameworks.Add(new FrameworkView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), [], []));
            }
        }

        var built = new List<FrameworkView>();
        foreach (var framework in frameworks)
        {
            var levels = await ListLevels(connection, framework.FrameworkId, cancellationToken).ConfigureAwait(false);
            var profiles = await ListProfiles(connection, framework.FrameworkId, cancellationToken).ConfigureAwait(false);
            built.Add(framework with { Levels = levels, Profiles = profiles });
        }

        return built;
    }

    public async Task<JobProfileDescriptor?> FindProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT profile.profile_id, profile.code, profile.display_name,
                   framework.code, framework.display_name,
                   level.code, level.display_name, level.rank
            FROM job_architecture.job_profile AS profile
            JOIN job_architecture.framework AS framework ON framework.framework_id = profile.framework_id
            JOIN job_architecture.level AS level ON level.level_id = profile.level_id
            WHERE profile.profile_id = $1
            """);
        command.Parameters.AddWithValue(profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new JobProfileDescriptor(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7));
    }

    private static async Task<Guid> UpsertFramework(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrameworkImport framework,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO job_architecture.framework (framework_id, code, display_name)
            VALUES ($1, $2, $3)
            ON CONFLICT (code) DO UPDATE SET display_name = EXCLUDED.display_name
            RETURNING framework_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(framework.Code.Trim());
        command.Parameters.AddWithValue(framework.Name.Trim());
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task<Guid> UpsertLevel(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid frameworkId,
        LevelImport level,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO job_architecture.level (level_id, framework_id, code, display_name, rank)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (framework_id, code) DO UPDATE
                SET display_name = EXCLUDED.display_name,
                    rank = EXCLUDED.rank
            RETURNING level_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(frameworkId);
        command.Parameters.AddWithValue(level.Code.Trim());
        command.Parameters.AddWithValue(level.Name.Trim());
        command.Parameters.AddWithValue(level.Rank);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task UpsertProfile(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid frameworkId,
        Guid levelId,
        ProfileImport profile,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO job_architecture.job_profile (profile_id, framework_id, level_id, code, display_name)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (code) DO UPDATE
                SET display_name = EXCLUDED.display_name,
                    framework_id = EXCLUDED.framework_id,
                    level_id = EXCLUDED.level_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(frameworkId);
        command.Parameters.AddWithValue(levelId);
        command.Parameters.AddWithValue(profile.Code.Trim());
        command.Parameters.AddWithValue(profile.Name.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<FrameworkLevel>> ListLevels(
        NpgsqlConnection connection,
        Guid frameworkId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT level_id, code, display_name, rank
            FROM job_architecture.level
            WHERE framework_id = $1
            ORDER BY rank, code
            """,
            connection);
        command.Parameters.AddWithValue(frameworkId);
        var levels = new List<FrameworkLevel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            levels.Add(new FrameworkLevel(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return levels;
    }

    private static async Task<IReadOnlyList<FrameworkProfile>> ListProfiles(
        NpgsqlConnection connection,
        Guid frameworkId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT profile.profile_id, profile.code, profile.display_name, level.code, level.display_name, level.rank
            FROM job_architecture.job_profile AS profile
            JOIN job_architecture.level AS level ON level.level_id = profile.level_id
            WHERE profile.framework_id = $1
            ORDER BY level.rank, profile.display_name
            """,
            connection);
        command.Parameters.AddWithValue(frameworkId);
        var profiles = new List<FrameworkProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            profiles.Add(new FrameworkProfile(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5)));
        }

        return profiles;
    }
}
