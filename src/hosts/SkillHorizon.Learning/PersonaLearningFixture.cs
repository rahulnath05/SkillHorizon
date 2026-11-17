using System.Text;
using Npgsql;

namespace SkillHorizon.Learning;

public sealed class PersonaLearningFixture
{
    private static readonly Guid AshaSittingId = Guid.Parse("a11a0002-0000-4000-8000-000000000002");
    private static readonly Guid RubricItemId = Guid.Parse("a11a0002-0000-4000-8000-000000000021");
    private static readonly Guid SinglePathId = Guid.Parse("b22b0001-0000-4000-8000-000000000001");
    private static readonly Guid AshaPathId = Guid.Parse("b22b0001-0000-4000-8000-000000000002");
    private static readonly Guid DevPathId = Guid.Parse("b22b0001-0000-4000-8000-000000000003");
    private static readonly Guid SingleSqlStep = Guid.Parse("b22b0001-0000-4000-8000-000000000011");
    private static readonly Guid SingleModelingStep = Guid.Parse("b22b0001-0000-4000-8000-000000000012");
    private static readonly Guid AshaSqlStep = Guid.Parse("b22b0001-0000-4000-8000-000000000021");
    private static readonly Guid DevModelingStep = Guid.Parse("b22b0001-0000-4000-8000-000000000031");
    private static readonly Guid SingleEvidenceId = Guid.Parse("b22b0001-0000-4000-8000-000000000041");
    private static readonly Guid AshaEvidenceId = Guid.Parse("b22b0001-0000-4000-8000-000000000042");

    private const string RubricPrompt =
        "A partition rebuild takes 40 minutes and will change a revenue figure that finance signed off yesterday. Say what you do, and in what order.";

    private const string RubricBody =
        "1|Held|Describes the change without an order\n2|Reached|Reproduces the difference before telling finance\n3|Advanced|Holds the rebuild and keeps the old partition readable\n4|Expert|Names the owner, the window, and the read path";

    private const string MarkedResponse =
        "I would not rebuild it in place during the day. First I would reproduce the difference on a copy, then tell the finance owner before the number moves, and hold the rebuild to the next close window.";

    private readonly NpgsqlDataSource _assessment;
    private readonly NpgsqlDataSource _progress;
    private readonly string _domainConnectionString;
    private readonly string _evidenceRoot;

    public PersonaLearningFixture(
        NpgsqlDataSource assessment,
        NpgsqlDataSource progress,
        string domainConnectionString,
        string evidenceRoot)
    {
        _assessment = assessment;
        _progress = progress;
        _domainConnectionString = domainConnectionString;
        _evidenceRoot = evidenceRoot;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_domainConnectionString))
            return;

        await EnsureStudioAsync(cancellationToken).ConfigureAwait(false);
        var wave = await WaitForWaveAsync(cancellationToken).ConfigureAwait(false);
        if (wave is null)
            return;

        await EnsureSittingsAsync(wave.Value, cancellationToken).ConfigureAwait(false);
        await EnsureSnapshotsAsync(wave.Value.WaveId, cancellationToken).ConfigureAwait(false);
        await EnsurePathsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureProgressionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureStudioAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (var item = new NpgsqlCommand(
            """
            INSERT INTO assessment.item (item_id, skill_code, prompt, difficulty)
            VALUES ($1, 'sql', $2, 3)
            ON CONFLICT (item_id) DO NOTHING
            """,
            connection))
        {
            item.Parameters.AddWithValue(RubricItemId);
            item.Parameters.AddWithValue(RubricPrompt);
            await item.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var rubric = new NpgsqlCommand(
            """
            INSERT INTO assessment.rubric (item_id, body)
            VALUES ($1, $2)
            ON CONFLICT (item_id) DO NOTHING
            """,
            connection))
        {
            rubric.Parameters.AddWithValue(RubricItemId);
            rubric.Parameters.AddWithValue(RubricBody);
            await rubric.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (domain, name, credentials) in Panel())
        {
            await using var expert = new NpgsqlCommand(
                """
                INSERT INTO assessment.sme (sme_id, domain_name, expert_name, credentials)
                VALUES (gen_random_uuid(), $1, $2, $3)
                ON CONFLICT (domain_name) DO NOTHING
                """,
                connection);
            expert.Parameters.AddWithValue(domain);
            expert.Parameters.AddWithValue(name);
            expert.Parameters.AddWithValue(credentials);
            await expert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureSittingsAsync((Guid WaveId, Guid BenchmarkVersionId) wave, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var open = await ScalarAsync(
            connection,
            "SELECT sitting_id FROM assessment.sitting WHERE subject_id = 'person-single' AND wave_id = $1",
            wave.WaveId,
            cancellationToken).ConfigureAwait(false);
        if (open is null)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO assessment.sitting (
                    sitting_id, authorization_id, token_hash, subject_id, wave_id, benchmark_version_id,
                    scale, status, opened_at)
                VALUES ($1, $2, 'persona-open-person-single', 'person-single', $3, $4, '[]'::jsonb, 'open', now())
                ON CONFLICT (subject_id, wave_id) DO NOTHING
                """,
                connection);
            insert.Parameters.AddWithValue(Guid.Parse("a11a0002-0000-4000-8000-000000000001"));
            insert.Parameters.AddWithValue(Guid.Parse("a11a0001-0000-4000-8000-000000000111"));
            insert.Parameters.AddWithValue(wave.WaveId);
            insert.Parameters.AddWithValue(wave.BenchmarkVersionId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var scale = await ScalarTextAsync(
            connection,
            "SELECT scale::text FROM assessment.sitting WHERE subject_id = 'person-single' AND wave_id = $1",
            wave.WaveId,
            cancellationToken).ConfigureAwait(false) ?? "[]";

        await using (var sitting = new NpgsqlCommand(
            """
            INSERT INTO assessment.sitting (
                sitting_id, authorization_id, token_hash, subject_id, wave_id, benchmark_version_id,
                scale, status, opened_at, sealed_at)
            VALUES (
                $1, $2, 'persona-sealed-asha-rao', 'asha-rao', $3, $4,
                $5::jsonb, 'sealed', now() - interval '20 minutes', now())
            ON CONFLICT (subject_id, wave_id) DO NOTHING
            """,
            connection))
        {
            sitting.Parameters.AddWithValue(AshaSittingId);
            sitting.Parameters.AddWithValue(Guid.Parse("a11a0001-0000-4000-8000-000000000112"));
            sitting.Parameters.AddWithValue(wave.WaveId);
            sitting.Parameters.AddWithValue(wave.BenchmarkVersionId);
            sitting.Parameters.AddWithValue(scale);
            await sitting.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var mark = new NpgsqlCommand(
            """
            INSERT INTO assessment.mark (sitting_id, item_id, response_body, band, marked_at)
            VALUES ($1, $2, $3, 3, now())
            ON CONFLICT (sitting_id, item_id) DO NOTHING
            """,
            connection);
        mark.Parameters.AddWithValue(AshaSittingId);
        mark.Parameters.AddWithValue(RubricItemId);
        mark.Parameters.AddWithValue(MarkedResponse);
        await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSnapshotsAsync(Guid waveId, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        int completed;
        int open;
        int duration;
        await using (var count = new NpgsqlCommand(
            """
            SELECT
                count(*) FILTER (WHERE status = 'sealed'),
                count(*) FILTER (WHERE status = 'open'),
                coalesce(sum(GREATEST(0, EXTRACT(EPOCH FROM (sealed_at - opened_at)))) FILTER (WHERE status = 'sealed' AND sealed_at IS NOT NULL), 0)::integer
            FROM assessment.sitting
            """,
            connection))
        await using (var reader = await count.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            completed = Convert.ToInt32(reader.GetValue(0));
            open = Convert.ToInt32(reader.GetValue(1));
            duration = Convert.ToInt32(reader.GetValue(2));
        }

        await InsertSnapshotAsync(connection, "operations", null, completed, open, duration, cancellationToken).ConfigureAwait(false);
        await InsertSnapshotAsync(connection, "scorecard", null, completed, open, duration, cancellationToken).ConfigureAwait(false);

        int pilotCompleted;
        int pilotOpen;
        int pilotDuration;
        await using (var count = new NpgsqlCommand(
            """
            SELECT
                count(*) FILTER (WHERE status = 'sealed'),
                count(*) FILTER (WHERE status = 'open'),
                coalesce(sum(GREATEST(0, EXTRACT(EPOCH FROM (sealed_at - opened_at)))) FILTER (WHERE status = 'sealed' AND sealed_at IS NOT NULL), 0)::integer
            FROM assessment.sitting
            WHERE wave_id = $1
            """,
            connection))
        {
            count.Parameters.AddWithValue(waveId);
            await using var reader = await count.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            pilotCompleted = Convert.ToInt32(reader.GetValue(0));
            pilotOpen = Convert.ToInt32(reader.GetValue(1));
            pilotDuration = Convert.ToInt32(reader.GetValue(2));
        }

        await InsertSnapshotAsync(connection, "pilot", waveId, pilotCompleted, pilotOpen, pilotDuration, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertSnapshotAsync(
        NpgsqlConnection connection,
        string kind,
        Guid? waveId,
        int completed,
        int open,
        int duration,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO assessment.programme_snapshot (
                snapshot_id, kind, wave_id, as_of, completed_count, open_count, duration_seconds)
            SELECT gen_random_uuid(), $1, $2, now(), $3, $4, $5
            WHERE NOT EXISTS (
                SELECT 1 FROM assessment.programme_snapshot
                WHERE kind = $1 AND wave_id IS NOT DISTINCT FROM $2)
            """,
            connection);
        command.Parameters.AddWithValue(kind);
        command.Parameters.Add(new NpgsqlParameter { Value = waveId.HasValue ? waveId.Value : DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        command.Parameters.AddWithValue(completed);
        command.Parameters.AddWithValue(open);
        command.Parameters.AddWithValue(duration);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePathsAsync(CancellationToken cancellationToken)
    {
        await using var domain = await OpenDomainAsync(cancellationToken).ConfigureAwait(false);
        await using var progress = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var single = await LatestResultAsync(domain, "person-single", cancellationToken).ConfigureAwait(false);
        var asha = await LatestResultAsync(domain, "asha-rao", cancellationToken).ConfigureAwait(false);
        var dev = await LatestResultAsync(domain, "dev-kapoor", cancellationToken).ConfigureAwait(false);
        if (single is not null)
        {
            var pathId = await EnsurePathAsync(progress, SinglePathId, "person-single", single, cancellationToken).ConfigureAwait(false);
            await EnsureStepAsync(progress, SingleSqlStep, pathId, "person-single", single.BenchmarkVersionId, "sql", "SQL analysis", 21, cancellationToken).ConfigureAwait(false);
            await EnsureStepAsync(progress, SingleModelingStep, pathId, "person-single", single.BenchmarkVersionId, "modeling", "Data modeling", 28, cancellationToken).ConfigureAwait(false);
            await EnsureEvidenceAsync(progress, SingleEvidenceId, SingleSqlStep, "persona-sql-note", "sql-analysis-note.txt", "SQL analysis note for the data analyst path.", cancellationToken).ConfigureAwait(false);
        }

        if (asha is not null)
        {
            var pathId = await EnsurePathAsync(progress, AshaPathId, "asha-rao", asha, cancellationToken).ConfigureAwait(false);
            await EnsureStepAsync(progress, AshaSqlStep, pathId, "asha-rao", asha.BenchmarkVersionId, "sql", "SQL analysis", 21, cancellationToken).ConfigureAwait(false);
            await EnsureEvidenceAsync(progress, AshaEvidenceId, AshaSqlStep, "persona-asha-readout", "client-readout.txt", "Client readout awaiting the manager's check.", cancellationToken).ConfigureAwait(false);
        }

        if (dev is not null)
        {
            var pathId = await EnsurePathAsync(progress, DevPathId, "dev-kapoor", dev, cancellationToken).ConfigureAwait(false);
            await EnsureStepAsync(progress, DevModelingStep, pathId, "dev-kapoor", dev.BenchmarkVersionId, "modeling", "Data modeling", 14, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<Guid> EnsurePathAsync(
        NpgsqlConnection progress,
        Guid pathId,
        string subjectId,
        ResultRow result,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.path (path_id, subject_id, result_id, benchmark_version_id, created_at)
            VALUES ($1, $2, $3, $4, now())
            ON CONFLICT (result_id, benchmark_version_id) DO UPDATE SET subject_id = EXCLUDED.subject_id
            RETURNING path_id
            """,
            progress);
        command.Parameters.AddWithValue(pathId);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(result.ResultId);
        command.Parameters.AddWithValue(result.BenchmarkVersionId);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task EnsureStepAsync(
        NpgsqlConnection progress,
        Guid stepId,
        Guid pathId,
        string subjectId,
        Guid benchmarkVersionId,
        string skillCode,
        string title,
        int dueInDays,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.step (
                step_id, path_id, subject_id, provider, title, skill_code, benchmark_version_id, status, recorded_at, due_on)
            SELECT $1, $2, $3, 'external', $4, $5, $6, 'assigned', now(), (timezone('utc', now()))::date + $7
            WHERE NOT EXISTS (
                SELECT 1 FROM progress.step AS step
                WHERE step.path_id = $2 AND step.skill_code = $5)
            """,
            progress);
        command.Parameters.AddWithValue(stepId);
        command.Parameters.AddWithValue(pathId);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(title);
        command.Parameters.AddWithValue(skillCode);
        command.Parameters.AddWithValue(benchmarkVersionId);
        command.Parameters.AddWithValue(dueInDays);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureEvidenceAsync(
        NpgsqlConnection progress,
        Guid evidenceId,
        Guid stepId,
        string objectKey,
        string fileName,
        string text,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Directory.CreateDirectory(_evidenceRoot);
        var path = Path.Combine(_evidenceRoot, objectKey);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.evidence (
                evidence_id, step_id, object_key, file_name, content_type, byte_length, recorded_at)
            SELECT $1, $2, $3, $4, 'application/octet-stream', $5, now()
            WHERE EXISTS (SELECT 1 FROM progress.step WHERE step_id = $2)
              AND NOT EXISTS (SELECT 1 FROM progress.evidence WHERE evidence_id = $1 OR step_id = $2)
            """,
            progress);
        command.Parameters.AddWithValue(evidenceId);
        command.Parameters.AddWithValue(stepId);
        command.Parameters.AddWithValue(objectKey);
        command.Parameters.AddWithValue(fileName);
        command.Parameters.AddWithValue(bytes.Length);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureProgressionAsync(CancellationToken cancellationToken)
    {
        var subjects = new[] { "person-single", "asha-rao", "dev-kapoor" };
        int paths;
        int steps;
        int open;
        await using (var progress = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        await using (var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM progress.path WHERE subject_id = ANY($1)),
                (SELECT count(*) FROM progress.step WHERE subject_id = ANY($1)),
                (SELECT count(*) FROM progress.step WHERE subject_id = ANY($1) AND status = 'assigned')
            """,
            progress))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = subjects, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            paths = Convert.ToInt32(reader.GetValue(0));
            steps = Convert.ToInt32(reader.GetValue(1));
            open = Convert.ToInt32(reader.GetValue(2));
        }

        await using var domain = await OpenDomainAsync(cancellationToken).ConfigureAwait(false);
        await using var update = new NpgsqlCommand(
            """
            INSERT INTO readiness.progression (practice_code, as_of, path_count, step_count, open_step_count)
            VALUES ('data', now(), $1, $2, $3)
            ON CONFLICT (practice_code) DO UPDATE SET
                as_of = EXCLUDED.as_of,
                path_count = EXCLUDED.path_count,
                step_count = EXCLUDED.step_count,
                open_step_count = EXCLUDED.open_step_count
            """,
            domain);
        update.Parameters.AddWithValue(paths);
        update.Parameters.AddWithValue(steps);
        update.Parameters.AddWithValue(open);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Guid WaveId, Guid BenchmarkVersionId)?> WaitForWaveAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var wave = await WaveAsync(cancellationToken).ConfigureAwait(false);
            if (wave is not null)
                return wave;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<(Guid WaveId, Guid BenchmarkVersionId)?> WaveAsync(CancellationToken cancellationToken)
    {
        await using var domain = await OpenDomainAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT wave.wave_id, wave.benchmark_version_id
            FROM activation.admission AS admission
            JOIN activation.wave AS wave ON wave.wave_id = admission.wave_id
            WHERE admission.subject_id = 'person-single'
            ORDER BY wave.created_at DESC
            LIMIT 1
            """,
            domain);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return (reader.GetGuid(0), reader.GetGuid(1));
    }

    private async Task<ResultRow?> LatestResultAsync(NpgsqlConnection domain, string subjectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT result_id, benchmark_version_id
            FROM readiness.sealed_result
            WHERE subject_id = $1
            ORDER BY sealed_at DESC
            LIMIT 1
            """,
            domain);
        command.Parameters.AddWithValue(subjectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new ResultRow(reader.GetGuid(0), reader.GetGuid(1));
    }

    private async Task<NpgsqlConnection> OpenDomainAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_domainConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<Guid?> ScalarAsync(NpgsqlConnection connection, string sql, Guid parameter, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(parameter);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is Guid id ? id : null;
    }

    private static async Task<string?> ScalarTextAsync(NpgsqlConnection connection, string sql, Guid parameter, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(parameter);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found as string;
    }

    private static (string Domain, string Name, string Credentials)[] Panel() =>
    [
        ("Data", "Ananya Rao", "15 yrs data platform; ex-Head of Data Engineering; MSc Statistics; dbt Certified"),
        ("Engineering", "Leena Iyer", "18 yrs software engineering; Principal Engineer; co-author of two internal engineering standards"),
        ("Cloud", "Karan Mehta", "12 yrs cloud infrastructure; AWS Solutions Architect Professional; Azure Solutions Architect Expert"),
        ("Cross-group", "Sunita Varma", "Chartered psychologist (BPS); 20 yrs psychometrics; sets the rubric standard")
    ];

    private sealed record ResultRow(Guid ResultId, Guid BenchmarkVersionId);
}
