using Npgsql;
using SkillHorizon.Audit;

namespace SkillHorizon.Readiness;

public sealed class PostgresDeclaredLevel : IDeclaredLevel
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;

    public PostgresDeclaredLevel(NpgsqlDataSource dataSource, IAuditLog audit)
    {
        _dataSource = dataSource;
        _audit = audit;
    }

    public async Task<DeclaredComparison> DeclareAsync(
        string subjectId,
        string skillCode,
        int declaredRank,
        string declaredName,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subject = subjectId.Trim();
        var skill = skillCode.Trim();
        var name = declaredName.Trim();
        if (subject.Length == 0 || skill.Length == 0 || name.Length == 0)
            throw new InvalidOperationException("A declaration names a person, a skill, and a level.");
        if (declaredRank < 1)
            throw new InvalidOperationException("A declared level is a positive rank.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var exists = new NpgsqlCommand("SELECT 1 FROM workforce.person WHERE employee_id = $1", connection);
        exists.Parameters.AddWithValue(subject);
        if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException("That person is not in this zone.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Guid declarationId;
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO readiness.declaration (
                declaration_id, subject_id, skill_code, declared_rank, declared_name, declared_at)
            VALUES ($1, $2, $3, $4, $5, now())
            ON CONFLICT (subject_id, skill_code) DO UPDATE SET
                declared_rank = EXCLUDED.declared_rank,
                declared_name = EXCLUDED.declared_name,
                declared_at = now()
            RETURNING declaration_id
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(subject);
            command.Parameters.AddWithValue(skill);
            command.Parameters.AddWithValue(declaredRank);
            command.Parameters.AddWithValue(name);
            declarationId = (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(actorId, "readiness.declare", "readiness.declaration", declarationId, correlationId, "{\"declared\":1}"),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var rows = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return rows.Single(row => row.SubjectId == subject && row.SkillCode == skill);
    }

    public async Task<IReadOnlyList<DeclaredComparison>> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            WITH assessed AS (
                SELECT DISTINCT ON (result.subject_id, proficiency.skill_code)
                    result.subject_id,
                    proficiency.skill_code,
                    proficiency.proficiency_rank,
                    proficiency.proficiency_name
                FROM readiness.proficiency AS proficiency
                JOIN readiness.sealed_result AS result ON result.result_id = proficiency.result_id
                ORDER BY result.subject_id, proficiency.skill_code, result.sealed_at DESC
            )
            SELECT
                COALESCE(declaration.subject_id, assessed.subject_id),
                COALESCE(declaration.skill_code, assessed.skill_code),
                declaration.declared_rank,
                declaration.declared_name,
                assessed.proficiency_rank,
                assessed.proficiency_name
            FROM readiness.declaration AS declaration
            FULL OUTER JOIN assessed
                ON assessed.subject_id = declaration.subject_id
               AND assessed.skill_code = declaration.skill_code
            ORDER BY 1, 2
            """,
            connection);
        var rows = new List<DeclaredComparison>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DeclaredComparison(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }
}
