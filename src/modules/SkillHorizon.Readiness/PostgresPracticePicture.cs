using Npgsql;

namespace SkillHorizon.Readiness;

public sealed class PostgresPracticePicture
{
    public async Task WriteAsync(
        NpgsqlConnection connection,
        string practiceCode,
        IProgressCensus progress,
        CancellationToken cancellationToken)
    {
        var members = await LoadMembers(connection, practiceCode, cancellationToken).ConfigureAwait(false);
        var cells = await LoadCells(connection, practiceCode, cancellationToken).ConfigureAwait(false);
        var counts = await progress.CountAsync(members.Select(member => member.EmployeeId).ToArray(), cancellationToken).ConfigureAwait(false);
        var asOf = DateTimeOffset.UtcNow;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await WriteHeatmap(connection, transaction, practiceCode, asOf, cells, cancellationToken).ConfigureAwait(false);
        await WriteProgression(connection, transaction, practiceCode, asOf, counts, cancellationToken).ConfigureAwait(false);
        await WriteCuts(connection, transaction, practiceCode, asOf, members, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<MemberRow>> LoadMembers(NpgsqlConnection connection, string practiceCode, CancellationToken cancellationToken)
    {
        var members = new List<MemberRow>();
        await using var command = new NpgsqlCommand(
            """
            WITH members AS (
                SELECT DISTINCT member.employee_id,
                       person.country_code,
                       level.display_name AS talent_group,
                       cohort.job_profile_id
                FROM attestation.cohort AS cohort
                JOIN attestation.cohort_member AS member ON member.cohort_id = cohort.cohort_id
                JOIN workforce.person AS person ON person.employee_id = member.employee_id
                JOIN job_architecture.job_profile AS profile ON profile.profile_id = cohort.job_profile_id
                JOIN job_architecture.level AS level ON level.level_id = profile.level_id
                WHERE cohort.practice_code = $1
                  AND person.employment_status = 'active'
            ),
            latest AS (
                SELECT DISTINCT ON (result.subject_id) result.subject_id, result.result_id
                FROM readiness.sealed_result AS result
                JOIN members ON members.employee_id = result.subject_id
                ORDER BY result.subject_id, result.sealed_at DESC
            ),
            gap_people AS (
                SELECT DISTINCT members.employee_id, members.job_profile_id
                FROM members
                JOIN latest ON latest.subject_id = members.employee_id
                JOIN benchmark.version AS published
                    ON published.job_profile_id = members.job_profile_id
                   AND published.status = 'published'
                JOIN benchmark.target AS target ON target.benchmark_version_id = published.benchmark_version_id
                LEFT JOIN readiness.proficiency AS measured
                    ON measured.result_id = latest.result_id
                   AND measured.skill_code = target.skill_code
                WHERE COALESCE(measured.proficiency_rank, 0) < target.proficiency_rank
            )
            SELECT members.employee_id,
                   members.country_code,
                   members.talent_group,
                   latest.subject_id IS NOT NULL,
                   gap_people.employee_id IS NOT NULL
            FROM members
            LEFT JOIN latest ON latest.subject_id = members.employee_id
            LEFT JOIN gap_people
                ON gap_people.employee_id = members.employee_id
               AND gap_people.job_profile_id = members.job_profile_id
            """,
            connection);
        command.Parameters.AddWithValue(practiceCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            members.Add(new MemberRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4)));
        }

        return members;
    }

    private static async Task<List<HeatmapCellRow>> LoadCells(NpgsqlConnection connection, string practiceCode, CancellationToken cancellationToken)
    {
        var cells = new List<HeatmapCellRow>();
        await using var command = new NpgsqlCommand(
            """
            SELECT DISTINCT ON (target.skill_code, level.code)
                   target.skill_code,
                   target.skill_name,
                   level.code,
                   level.display_name,
                   level.rank,
                   target.proficiency_rank,
                   target.proficiency_name
            FROM attestation.baseline AS baseline
            JOIN attestation.seal AS seal ON seal.pack_id = baseline.pack_id
            JOIN attestation.sealed_benchmark AS sealed ON sealed.seal_id = seal.seal_id
            JOIN benchmark.target AS target ON target.benchmark_version_id = sealed.benchmark_version_id
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = sealed.job_profile_id
            JOIN job_architecture.level AS level ON level.level_id = profile.level_id
            WHERE baseline.practice_code = $1
            ORDER BY target.skill_code, level.code, target.proficiency_rank DESC, target.skill_name
            """,
            connection);
        command.Parameters.AddWithValue(practiceCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cells.Add(new HeatmapCellRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(6)));
        }

        return cells;
    }

    private static async Task WriteHeatmap(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string practiceCode,
        DateTimeOffset asOf,
        IReadOnlyList<HeatmapCellRow> cells,
        CancellationToken cancellationToken)
    {
        await using (var header = new NpgsqlCommand(
            """
            INSERT INTO readiness.heatmap (practice_code, as_of)
            VALUES ($1, $2)
            ON CONFLICT (practice_code) DO UPDATE SET as_of = EXCLUDED.as_of
            """,
            connection,
            transaction))
        {
            header.Parameters.AddWithValue(practiceCode);
            header.Parameters.AddWithValue(asOf);
            await header.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var clear = new NpgsqlCommand(
            "DELETE FROM readiness.heatmap_cell WHERE practice_code = $1",
            connection,
            transaction))
        {
            clear.Parameters.AddWithValue(practiceCode);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var cell in cells)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO readiness.heatmap_cell (
                    practice_code, skill_code, skill_name, level_code, level_name, level_rank, proficiency_rank, proficiency_name)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue(practiceCode);
            insert.Parameters.AddWithValue(cell.SkillCode);
            insert.Parameters.AddWithValue(cell.SkillName);
            insert.Parameters.AddWithValue(cell.LevelCode);
            insert.Parameters.AddWithValue(cell.LevelName);
            insert.Parameters.AddWithValue(cell.LevelRank);
            insert.Parameters.AddWithValue(cell.ProficiencyRank);
            insert.Parameters.AddWithValue(cell.ProficiencyName);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteProgression(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string practiceCode,
        DateTimeOffset asOf,
        ProgressCounts counts,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO readiness.progression (practice_code, as_of, path_count, step_count, open_step_count)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (practice_code) DO UPDATE
                SET as_of = EXCLUDED.as_of,
                    path_count = EXCLUDED.path_count,
                    step_count = EXCLUDED.step_count,
                    open_step_count = EXCLUDED.open_step_count
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(practiceCode);
        command.Parameters.AddWithValue(asOf);
        command.Parameters.AddWithValue(counts.PathCount);
        command.Parameters.AddWithValue(counts.StepCount);
        command.Parameters.AddWithValue(counts.OpenStepCount);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteCuts(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string practiceCode,
        DateTimeOffset asOf,
        IReadOnlyList<MemberRow> members,
        CancellationToken cancellationToken)
    {
        await using (var clear = new NpgsqlCommand(
            "DELETE FROM readiness.cut WHERE practice_code = $1",
            connection,
            transaction))
        {
            clear.Parameters.AddWithValue(practiceCode);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (kind, buckets) in new[] { ("country", Buckets(members, member => member.Country)), ("talent_group", Buckets(members, member => member.TalentGroup)) })
        {
            foreach (var (key, bucket) in buckets)
            {
                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO readiness.cut (practice_code, cut_kind, cut_key, as_of, member_count, measured_count, gap_count)
                    VALUES ($1, $2, $3, $4, $5, $6, $7)
                    """,
                    connection,
                    transaction);
                insert.Parameters.AddWithValue(practiceCode);
                insert.Parameters.AddWithValue(kind);
                insert.Parameters.AddWithValue(key);
                insert.Parameters.AddWithValue(asOf);
                insert.Parameters.AddWithValue(bucket.Members.Count);
                insert.Parameters.AddWithValue(bucket.Measured.Count);
                insert.Parameters.AddWithValue(bucket.Gaps.Count);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Dictionary<string, CutBucket> Buckets(IReadOnlyList<MemberRow> members, Func<MemberRow, string?> key)
    {
        var buckets = new Dictionary<string, CutBucket>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var name = key(member)?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            if (!buckets.TryGetValue(name, out var bucket))
            {
                bucket = new CutBucket();
                buckets[name] = bucket;
            }

            bucket.Members.Add(member.EmployeeId);
            if (member.Measured)
                bucket.Measured.Add(member.EmployeeId);
            if (member.Gap)
                bucket.Gaps.Add(member.EmployeeId);
        }

        return buckets;
    }

    private sealed record MemberRow(string EmployeeId, string? Country, string TalentGroup, bool Measured, bool Gap);

    private sealed record HeatmapCellRow(string SkillCode, string SkillName, string LevelCode, string LevelName, int LevelRank, int ProficiencyRank, string ProficiencyName);

    private sealed class CutBucket
    {
        public HashSet<string> Members { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Measured { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Gaps { get; } = new(StringComparer.Ordinal);
    }
}
