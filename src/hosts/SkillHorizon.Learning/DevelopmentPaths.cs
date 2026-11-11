using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SkillHorizon.Learning;

public sealed record PathStepView(
    Guid StepId,
    string SkillCode,
    string Title,
    string Provider,
    string Status,
    string? EvidenceName);

public sealed record DevelopmentPathView(Guid PathId, Guid BenchmarkVersionId, IReadOnlyList<PathStepView> Steps);

public sealed record TaskRow(Guid StepId, string Title, string SkillCode, DateOnly DueOn, string Status, string? EvidenceName);

public sealed record StoredValidation(Guid ValidationId, string ManagerEmployeeId, string ReportEmployeeId);

public sealed class DevelopmentPaths
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _progress;
    private readonly HttpClient _core;
    private readonly IEvidenceFiles _files;

    public DevelopmentPaths(NpgsqlDataSource progress, HttpClient core, IEvidenceFiles files)
    {
        _progress = progress;
        _core = core;
        _files = files;
    }

    public async Task<DevelopmentPathView> OpenAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        var plan = await Core<PathPlanBody>(HttpMethod.Post, "commands/paths/open", bearer, workspace, correlationId, new { }, cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var pathId = await EnsurePath(connection, subject, plan, cancellationToken).ConfigureAwait(false);
        foreach (var step in plan.Steps ?? [])
            await EnsureStep(connection, pathId, subject, plan, step, cancellationToken).ConfigureAwait(false);
        return await Read(connection, pathId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DevelopmentPathView?> ReadOwnAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var pathId = await LatestPath(connection, subject, cancellationToken).ConfigureAwait(false);
        return pathId is Guid id ? await Read(connection, id, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<IReadOnlyList<TaskRow>> ListTasksAsync(string bearer, string workspace, string correlationId, string? requestedSubject, CancellationToken cancellationToken)
    {
        var subject = await OwnedSubject(bearer, workspace, correlationId, requestedSubject, cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT step.step_id, step.title, step.skill_code, step.due_on, step.status, evidence.file_name
            FROM progress.step AS step
            LEFT JOIN LATERAL (
                SELECT file_name FROM progress.evidence
                WHERE step_id = step.step_id
                ORDER BY recorded_at DESC
                LIMIT 1) AS evidence ON true
            WHERE step.subject_id = $1
              AND step.provider = 'external'
              AND step.status = 'assigned'
              AND step.due_on IS NOT NULL
              AND step.path_id = (
                  SELECT latest.path_id
                  FROM progress.path AS latest
                  WHERE latest.subject_id = $1
                  ORDER BY latest.created_at DESC
                  LIMIT 1)
            ORDER BY step.due_on, step.title
            """,
            connection);
        command.Parameters.AddWithValue(subject);
        return await ReadTasks(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskRow> ReadTaskAsync(string bearer, string workspace, string correlationId, Guid stepId, string? requestedSubject, CancellationToken cancellationToken)
    {
        var subject = await OwnedSubject(bearer, workspace, correlationId, requestedSubject, cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var task = await FindTask(connection, stepId, subject, cancellationToken).ConfigureAwait(false);
        if (task is null)
            throw new UnauthorizedAccessException("Another person's task is not available in this workspace.");
        return task;
    }

    public async Task<TaskRow> CloseTaskAsync(
        string bearer,
        string workspace,
        string correlationId,
        Guid stepId,
        string? requestedSubject,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var subject = await OwnedSubject(bearer, workspace, correlationId, requestedSubject, cancellationToken).ConfigureAwait(false);
        await using (var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            var task = await FindTask(connection, stepId, subject, cancellationToken).ConfigureAwait(false);
            if (task is null)
                throw new UnauthorizedAccessException("Another person's task is not available in this workspace.");
            if (!string.Equals(task.Status, "assigned", StringComparison.Ordinal))
                throw new InvalidOperationException("That step is already closed.");
        }

        await AddEvidenceAsync(bearer, workspace, correlationId, stepId, fileName, content, cancellationToken).ConfigureAwait(false);
        await CompleteExternalAsync(bearer, workspace, correlationId, stepId, cancellationToken).ConfigureAwait(false);
        return await ReadTaskAsync(bearer, workspace, correlationId, stepId, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DevelopmentPathView> AddEvidenceAsync(
        string bearer,
        string workspace,
        string correlationId,
        Guid stepId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        if (content.Length == 0 || content.Length > 1_000_000)
            throw new InvalidOperationException("Evidence needs a file under one megabyte.");
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        var key = await _files.PutAsync(content, cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var pathId = await StepPath(connection, stepId, subject, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.evidence (evidence_id, step_id, object_key, file_name, content_type, byte_length, recorded_at)
            VALUES ($1, $2, $3, $4, 'application/octet-stream', $5, now())
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(stepId);
        command.Parameters.AddWithValue(key);
        command.Parameters.AddWithValue(string.IsNullOrWhiteSpace(fileName) ? "evidence" : Path.GetFileName(fileName));
        command.Parameters.AddWithValue(content.Length);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return await Read(connection, pathId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredValidation> ValidateAsync(
        string bearer,
        string workspace,
        string correlationId,
        string subjectId,
        Guid stepId,
        CancellationToken cancellationToken)
    {
        var link = await Core<LinkBody>(
            HttpMethod.Post,
            "commands/validations/authorize",
            bearer,
            workspace,
            correlationId,
            new { subjectId },
            cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await StepPath(connection, stepId, link.ReportEmployeeId, cancellationToken, transaction).ConfigureAwait(false);
        var existing = await FindValidation(connection, transaction, stepId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        var validationId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO progress.validation (validation_id, step_id, manager_employee_id, report_employee_id, recorded_at)
            VALUES ($1, $2, $3, $4, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(validationId);
            insert.Parameters.AddWithValue(stepId);
            insert.Parameters.AddWithValue(link.ManagerEmployeeId);
            insert.Parameters.AddWithValue(link.ReportEmployeeId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE progress.step SET status = 'completed', recorded_at = now() WHERE step_id = $1 AND subject_id = $2",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue(stepId);
            update.Parameters.AddWithValue(link.ReportEmployeeId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                throw new InvalidOperationException("That step is not on this person's path.");
        }

        await using (var audit = new NpgsqlCommand(
            """
            INSERT INTO progress.audit_record (
                audit_id, subject_id, action, record_type, record_id, correlation_id, detail, occurred_at)
            VALUES ($1, $2, 'validation.recorded', 'progress.validation', $3, $4, $5::jsonb, now())
            """,
            connection,
            transaction))
        {
            audit.Parameters.AddWithValue(Guid.NewGuid());
            audit.Parameters.AddWithValue(link.ManagerEmployeeId);
            audit.Parameters.AddWithValue(validationId);
            audit.Parameters.AddWithValue(string.IsNullOrWhiteSpace(correlationId) ? "validation" : correlationId);
            audit.Parameters.AddWithValue(JsonSerializer.Serialize(new
            {
                managerEmployeeId = link.ManagerEmployeeId,
                reportEmployeeId = link.ReportEmployeeId
            }, Json));
            await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new StoredValidation(validationId, link.ManagerEmployeeId, link.ReportEmployeeId);
    }

    public async Task CompleteExternalAsync(string bearer, string workspace, string correlationId, Guid stepId, CancellationToken cancellationToken)
    {
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE progress.step
            SET status = 'completed', recorded_at = now()
            WHERE step_id = $1 AND subject_id = $2 AND provider = 'external'
            """,
            connection);
        command.Parameters.AddWithValue(stepId);
        command.Parameters.AddWithValue(subject);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("That external step is not on this path.");
    }

    public async Task<IReadOnlyList<PendingEvidence>> ListPendingEvidenceAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        var team = await Core<TeamBody>(HttpMethod.Get, "workforce/team", bearer, workspace, correlationId, null, cancellationToken).ConfigureAwait(false);
        var people = team.People ?? [];
        if (people.Length == 0)
            return [];
        var names = people.ToDictionary(person => person.EmployeeId, person => person.DisplayName);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT step.step_id, step.subject_id, step.title, evidence.file_name
            FROM progress.step AS step
            JOIN LATERAL (
                SELECT file_name FROM progress.evidence
                WHERE step_id = step.step_id
                ORDER BY recorded_at DESC
                LIMIT 1) AS evidence ON true
            WHERE step.subject_id = ANY($1)
              AND step.path_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM progress.validation AS validation
                  WHERE validation.step_id = step.step_id)
            ORDER BY step.subject_id, step.title
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter { Value = names.Keys.ToArray(), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text });
        var pending = new List<PendingEvidence>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var subject = reader.GetString(1);
            pending.Add(new PendingEvidence(
                reader.GetGuid(0),
                subject,
                names[subject],
                reader.GetString(2),
                reader.GetString(3)));
        }

        return pending;
    }

    public async Task<IReadOnlyList<ReportPath>> TeamAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        var team = await Core<TeamBody>(HttpMethod.Get, "workforce/team", bearer, workspace, correlationId, null, cancellationToken).ConfigureAwait(false);
        var reports = new List<ReportPath>();
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var person in team.People ?? [])
        {
            var pathId = await LatestPath(connection, person.EmployeeId, cancellationToken).ConfigureAwait(false);
            var path = pathId is Guid id ? await Read(connection, id, cancellationToken).ConfigureAwait(false) : null;
            reports.Add(new ReportPath(person.EmployeeId, person.DisplayName, path));
        }

        return reports;
    }

    private static async Task<Guid> EnsurePath(NpgsqlConnection connection, string subjectId, PathPlanBody plan, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.path (path_id, subject_id, result_id, benchmark_version_id, created_at)
            VALUES ($1, $2, $3, $4, now())
            ON CONFLICT (result_id, benchmark_version_id) DO UPDATE SET subject_id = progress.path.subject_id
            RETURNING path_id
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(plan.ResultId);
        command.Parameters.AddWithValue(plan.BenchmarkVersionId);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task EnsureStep(NpgsqlConnection connection, Guid pathId, string subjectId, PathPlanBody plan, StepPlanBody step, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.step (
                step_id, path_id, subject_id, provider, title, skill_code, benchmark_version_id, status, recorded_at, due_on)
            SELECT $1, $2, $3, 'external', $4, $5, $6, 'assigned', now(), (timezone('utc', now()))::date + 28
            WHERE NOT EXISTS (
                SELECT 1 FROM progress.step
                WHERE path_id = $2 AND skill_code = $5)
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(pathId);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(step.SkillName);
        command.Parameters.AddWithValue(step.SkillCode);
        command.Parameters.AddWithValue(plan.BenchmarkVersionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DevelopmentPathView> Read(NpgsqlConnection connection, Guid pathId, CancellationToken cancellationToken)
    {
        Guid benchmarkVersionId;
        await using (var path = new NpgsqlCommand(
            "SELECT benchmark_version_id FROM progress.path WHERE path_id = $1",
            connection))
        {
            path.Parameters.AddWithValue(pathId);
            benchmarkVersionId = (Guid)(await path.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        var steps = new List<PathStepView>();
        await using var command = new NpgsqlCommand(
            """
            SELECT step.step_id, step.skill_code, step.title, step.provider, step.status, evidence.file_name
            FROM progress.step AS step
            LEFT JOIN LATERAL (
                SELECT file_name FROM progress.evidence
                WHERE step_id = step.step_id
                ORDER BY recorded_at DESC
                LIMIT 1) AS evidence ON true
            WHERE step.path_id = $1
            ORDER BY step.title
            """,
            connection);
        command.Parameters.AddWithValue(pathId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            steps.Add(new PathStepView(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return new DevelopmentPathView(pathId, benchmarkVersionId, steps);
    }

    private static async Task<Guid?> LatestPath(NpgsqlConnection connection, string subjectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT path_id FROM progress.path WHERE subject_id = $1 ORDER BY created_at DESC LIMIT 1",
            connection);
        command.Parameters.AddWithValue(subjectId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is Guid pathId ? pathId : null;
    }

    private static async Task<Guid> StepPath(
        NpgsqlConnection connection,
        Guid stepId,
        string subjectId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            "SELECT path_id FROM progress.step WHERE step_id = $1 AND subject_id = $2 AND path_id IS NOT NULL",
            connection,
            transaction);
        command.Parameters.AddWithValue(stepId);
        command.Parameters.AddWithValue(subjectId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is Guid pathId ? pathId : throw new InvalidOperationException("That step is not on this person's path.");
    }

    private static async Task<StoredValidation?> FindValidation(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid stepId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT validation_id, manager_employee_id, report_employee_id FROM progress.validation WHERE step_id = $1",
            connection,
            transaction);
        command.Parameters.AddWithValue(stepId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new StoredValidation(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    }

    private async Task<string> OwnedSubject(string bearer, string workspace, string correlationId, string? requestedSubject, CancellationToken cancellationToken)
    {
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(requestedSubject) && !string.Equals(requestedSubject, subject, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Another person's task is not available in this workspace.");
        return subject;
    }

    private static async Task<TaskRow?> FindTask(NpgsqlConnection connection, Guid stepId, string subjectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT step.step_id, step.title, step.skill_code, step.due_on, step.status, evidence.file_name
            FROM progress.step AS step
            LEFT JOIN LATERAL (
                SELECT file_name FROM progress.evidence
                WHERE step_id = step.step_id
                ORDER BY recorded_at DESC
                LIMIT 1) AS evidence ON true
            WHERE step.step_id = $1
              AND step.subject_id = $2
              AND step.provider = 'external'
              AND step.path_id IS NOT NULL
              AND step.due_on IS NOT NULL
            """,
            connection);
        command.Parameters.AddWithValue(stepId);
        command.Parameters.AddWithValue(subjectId);
        var rows = await ReadTasks(command, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<List<TaskRow>> ReadTasks(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var tasks = new List<TaskRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tasks.Add(new TaskRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                ReadDate(reader, 3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return tasks;
    }

    private static DateOnly ReadDate(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime time => DateOnly.FromDateTime(time),
            _ => throw new InvalidOperationException("A task due date could not be read.")
        };
    }

    private async Task<string> Subject(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        var body = await Core<SubjectBody>(HttpMethod.Get, "employee/subject", bearer, workspace, correlationId, null, cancellationToken).ConfigureAwait(false);
        return body.SubjectId;
    }

    private async Task<T> Core<T>(
        HttpMethod method,
        string path,
        string bearer,
        string workspace,
        string correlationId,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("X-Active-Workspace", workspace);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        using var response = await _core.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("This command is refused for the active workspace.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The core API refused this path command.");
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The core API refused this path command.");
    }

    private sealed record PathPlanBody(Guid ResultId, Guid BenchmarkVersionId, StepPlanBody[]? Steps);

    private sealed record StepPlanBody(string SkillCode, string SkillName, string Provider);

    private sealed record LinkBody(string ManagerEmployeeId, string ReportEmployeeId);

    private sealed record SubjectBody(string SubjectId);

    private sealed record TeamBody(TeamPerson[]? People);

    private sealed record TeamPerson(string EmployeeId, string DisplayName);
}

public sealed record PendingEvidence(Guid StepId, string SubjectId, string DisplayName, string Title, string FileName);

public sealed record ReportPath(string EmployeeId, string DisplayName, DevelopmentPathView? Path);
