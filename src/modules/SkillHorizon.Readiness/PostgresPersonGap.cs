using System.Text.Json;
using Npgsql;
using SkillHorizon.Audit;

namespace SkillHorizon.Readiness;

public sealed class PostgresPersonGap : IPersonGap
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;

    public PostgresPersonGap(NpgsqlDataSource dataSource, IAuditLog audit)
    {
        _dataSource = dataSource;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SkillGap>> ApplyAsync(
        Guid authorizationId,
        Guid resultId,
        IReadOnlyList<MeasuredSkill> skills,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (skills.Count == 0)
            throw new InvalidOperationException("A sealed result needs a proficiency for each measured skill.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string subjectId;
        Guid benchmarkVersionId;
        Guid waveId;
        await using (var grant = new NpgsqlCommand(
            """
            SELECT subject_id, benchmark_version_id, wave_id, consumed_at
            FROM activation.sitting_authorization
            WHERE authorization_id = $1
            FOR UPDATE
            """,
            connection,
            transaction))
        {
            grant.Parameters.AddWithValue(authorizationId);
            await using var reader = await grant.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("That sitting authorization does not exist.");
            subjectId = reader.GetString(0);
            benchmarkVersionId = reader.GetGuid(1);
            waveId = reader.GetGuid(2);
            if (!reader.IsDBNull(3))
            {
                await reader.CloseAsync().ConfigureAwait(false);
                var existing = await ExistingResult(connection, transaction, authorizationId, cancellationToken).ConfigureAwait(false);
                if (existing == resultId)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return await Gaps(connection, null, subjectId, cancellationToken).ConfigureAwait(false);
                }

                throw new InvalidOperationException("That sitting authorization is already sealed.");
            }
        }

        var targets = await Targets(connection, transaction, benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        var measured = skills.ToDictionary(skill => skill.SkillCode, StringComparer.Ordinal);
        if (measured.Count != skills.Count || targets.Count != measured.Count || targets.Any(target => !measured.ContainsKey(target)))
            throw new InvalidOperationException("A sealed result names each skill on the benchmark version.");

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO readiness.sealed_result (
                result_id, authorization_id, subject_id, benchmark_version_id, wave_id, sealed_at)
            VALUES ($1, $2, $3, $4, $5, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(resultId);
            insert.Parameters.AddWithValue(authorizationId);
            insert.Parameters.AddWithValue(subjectId);
            insert.Parameters.AddWithValue(benchmarkVersionId);
            insert.Parameters.AddWithValue(waveId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var skill in skills)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO readiness.proficiency (
                    result_id, skill_code, skill_name, proficiency_rank, proficiency_code, proficiency_name)
                VALUES ($1, $2, $3, $4, $5, $6)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue(resultId);
            insert.Parameters.AddWithValue(skill.SkillCode);
            insert.Parameters.AddWithValue(skill.SkillName);
            insert.Parameters.AddWithValue(skill.Rank);
            insert.Parameters.AddWithValue(skill.Code);
            insert.Parameters.AddWithValue(skill.Name);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var consume = new NpgsqlCommand(
            "UPDATE activation.sitting_authorization SET consumed_at = now() WHERE authorization_id = $1",
            connection,
            transaction))
        {
            consume.Parameters.AddWithValue(authorizationId);
            await consume.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(subjectId, "result.sealed", "readiness.sealed_result", resultId, correlationId, JsonSerializer.Serialize(skills, Json)),
            cancellationToken).ConfigureAwait(false);
        var gap = await Gaps(connection, transaction, subjectId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return gap;
    }

    public async Task<IReadOnlyList<SkillGap>> ReadAsync(string subjectId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await Gaps(connection, null, subjectId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PathPlan> PlanAsync(string subjectId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Guid resultId;
        Guid benchmarkVersionId;
        await using (var current = new NpgsqlCommand(
            """
            SELECT result.result_id, published.benchmark_version_id
            FROM readiness.sealed_result AS result
            JOIN benchmark.version AS sealed ON sealed.benchmark_version_id = result.benchmark_version_id
            JOIN benchmark.version AS published
                ON published.job_profile_id = sealed.job_profile_id
               AND published.status = 'published'
            WHERE result.subject_id = $1
            ORDER BY result.sealed_at DESC
            LIMIT 1
            """,
            connection))
        {
            current.Parameters.AddWithValue(subjectId);
            await using var reader = await current.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("A path needs a sealed result and a published benchmark.");
            resultId = reader.GetGuid(0);
            benchmarkVersionId = reader.GetGuid(1);
        }

        var steps = new List<PathStepPlan>();
        await using var command = new NpgsqlCommand(
            """
            SELECT target.skill_code, target.skill_name
            FROM benchmark.target AS target
            LEFT JOIN readiness.proficiency AS person
                ON person.result_id = $2 AND person.skill_code = target.skill_code
            WHERE target.benchmark_version_id = $1
              AND COALESCE(person.proficiency_rank, 0) < target.proficiency_rank
            ORDER BY target.skill_name
            """,
            connection);
        command.Parameters.AddWithValue(benchmarkVersionId);
        command.Parameters.AddWithValue(resultId);
        await using var gapReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await gapReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            steps.Add(new PathStepPlan(gapReader.GetString(0), gapReader.GetString(1), "external"));
        return new PathPlan(resultId, benchmarkVersionId, steps);
    }

    private static async Task<Guid> ExistingResult(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid authorizationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT result_id FROM readiness.sealed_result WHERE authorization_id = $1",
            connection,
            transaction);
        command.Parameters.AddWithValue(authorizationId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is Guid resultId ? resultId : Guid.Empty;
    }

    private static async Task<HashSet<string>> Targets(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid benchmarkVersionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT skill_code FROM benchmark.target WHERE benchmark_version_id = $1",
            connection,
            transaction);
        command.Parameters.AddWithValue(benchmarkVersionId);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            codes.Add(reader.GetString(0));
        return codes;
    }

    private static async Task<IReadOnlyList<SkillGap>> Gaps(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string subjectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT target.skill_name, COALESCE(person.proficiency_name, ''), COALESCE(person.proficiency_rank, 0),
                   target.proficiency_name, target.proficiency_rank
            FROM readiness.sealed_result AS result
            JOIN benchmark.target AS target ON target.benchmark_version_id = result.benchmark_version_id
            LEFT JOIN readiness.proficiency AS person
                ON person.result_id = result.result_id AND person.skill_code = target.skill_code
            WHERE result.subject_id = $1
              AND result.result_id = (
                  SELECT latest.result_id
                  FROM readiness.sealed_result AS latest
                  WHERE latest.subject_id = $1
                  ORDER BY latest.sealed_at DESC
                  LIMIT 1)
              AND COALESCE(person.proficiency_rank, 0) < target.proficiency_rank
            ORDER BY target.skill_name
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(subjectId);
        var gaps = new List<SkillGap>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            gaps.Add(new SkillGap(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return gaps;
    }
}
