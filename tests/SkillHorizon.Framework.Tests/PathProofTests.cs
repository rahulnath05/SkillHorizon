extern alias LearningHost;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Taxonomy;
using Learning = LearningHost::SkillHorizon.Learning;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class PathProofTests
{
    private const string EvidenceBytes = "zeta-evidence-bytes";

    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public PathProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task A_path_keeps_evidence_bytes_out_of_the_database_and_records_the_manager_in_force()
    {
        const string subject = "path-person";
        await Grant(subject, "employee");
        await Grant("other-manager", "people_manager");
        try
        {
            await Prove(subject);
        }
        finally
        {
            await Cleanup(subject);
        }
    }

    private async Task Prove(string subject)
    {
        await ClearDrafts();
        using var api = _infrastructure.Api.CreateClient();
        var budget = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 5 });
        Assert.Equal(HttpStatusCode.NoContent, budget.StatusCode);

        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        await jobs.ImportAsync(
            [new FrameworkImport(Code("field"), "Field", [new LevelImport(Code("apprentice"), "Apprentice", 1)], [new ProfileImport(Code("analyst"), "Field analyst", Code("apprentice"))])],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(new TaxonomyDraft(
            [new PracticeDraft(Code("practice"), "Field practice", [new CompetencyDraft(Code("competency"), "Field competency",
            [
                new SkillDraft(Code("met"), "Met measure"),
                new SkillDraft(Code("gap"), "Gap measure"),
                new SkillDraft(Code("later"), "Later measure")
            ])])],
            [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]), CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "path-taxonomy-" + _suffix, CancellationToken.None);
        var met = publishedTaxonomy.Skills.Single(skill => skill.Code == Code("met"));
        var gap = publishedTaxonomy.Skills.Single(skill => skill.Code == Code("gap"));
        var later = publishedTaxonomy.Skills.Single(skill => skill.Code == Code("later"));
        var first = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId,
        [
            new TargetDraft(met.SkillId, 1),
            new TargetDraft(gap.SkillId, 2),
            new TargetDraft(later.SkillId, 2)
        ]), CancellationToken.None);
        var published = await benchmarks.PublishAsync(first.BenchmarkVersionId, "person-ld", "path-benchmark-" + _suffix, CancellationToken.None);

        var admitted = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
        {
            benchmarkVersionId = published.BenchmarkVersionId,
            subjectIds = new[] { subject }
        });
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        var waveId = JsonSerializer.Deserialize<JsonElement>(await admitted.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();
        var authorized = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", subject, "employee", new { waveId });
        var grant = JsonSerializer.Deserialize<JsonElement>(await authorized.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var measured = new[]
        {
            new { skillCode = met.Code, skillName = met.Name, rank = 1, code = Code("held"), name = "Held" },
            new { skillCode = gap.Code, skillName = gap.Name, rank = 1, code = Code("held"), name = "Held" },
            new { skillCode = later.Code, skillName = later.Name, rank = 1, code = Code("held"), name = "Held" }
        };
        var applied = await LearningKey(api, new { authorizationId = grant.GetProperty("authorizationId").GetGuid(), resultId = Guid.NewGuid(), proficiencies = measured });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        using var learning = _infrastructure.Learning.CreateClient();
        var bearer = await _infrastructure.IssueTokenAsync(subject, "LE-LOCAL");
        var opened = await LearningSend(learning, HttpMethod.Post, "/paths", bearer, "employee", new { });
        var openedBody = await opened.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var path = JsonSerializer.Deserialize<JsonElement>(openedBody);
        var steps = path.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(2, steps.Length);
        Assert.DoesNotContain(openedBody, "Met measure", StringComparison.Ordinal);
        Assert.Contains("Gap measure", openedBody, StringComparison.Ordinal);
        Assert.Contains("Later measure", openedBody, StringComparison.Ordinal);
        Assert.Contains("external", openedBody, StringComparison.Ordinal);
        var pathId = path.GetProperty("pathId").GetGuid();
        var again = await LearningSend(learning, HttpMethod.Post, "/paths", bearer, "employee", new { });
        Assert.Equal(pathId, JsonSerializer.Deserialize<JsonElement>(await again.Content.ReadAsStringAsync()).GetProperty("pathId").GetGuid());
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.path WHERE path_id = $1", pathId));

        var gapStep = steps.Single(step => step.GetProperty("title").GetString() == "Gap measure").GetProperty("stepId").GetGuid();
        var laterStep = steps.Single(step => step.GetProperty("title").GetString() == "Later measure").GetProperty("stepId").GetGuid();
        var evidence = await LearningSend(learning, HttpMethod.Post, "/paths/evidence", bearer, "employee", new
        {
            stepId = gapStep,
            fileName = "notes.txt",
            contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(EvidenceBytes))
        });
        var evidenceBody = await evidence.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        Assert.Contains("notes.txt", evidenceBody, StringComparison.Ordinal);
        Assert.DoesNotContain(EvidenceBytes, evidenceBody, StringComparison.Ordinal);
        Assert.Equal(0, await Scan(_infrastructure.ProgressConnection, EvidenceBytes));
        var objectKey = await ProgressText("SELECT object_key FROM progress.evidence WHERE file_name = 'notes.txt' ORDER BY recorded_at DESC LIMIT 1");
        var stored = await _infrastructure.Learning.Services.GetRequiredService<Learning.IEvidenceFiles>().ReadAsync(objectKey, CancellationToken.None);
        Assert.Equal(EvidenceBytes, Encoding.UTF8.GetString(stored!));

        var managerBearer = await _infrastructure.IssueTokenAsync("person-multi", "LE-LOCAL");
        var refused = await LearningSend(learning, HttpMethod.Post, "/validations", managerBearer, "people_manager", new { subjectId = subject, stepId = gapStep });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, await ProgressCount("SELECT count(*) FROM progress.validation"));

        await Link("path-leaver", "person-multi", "closed");
        var closed = await LearningSend(learning, HttpMethod.Post, "/validations", managerBearer, "people_manager", new { subjectId = "path-leaver", stepId = laterStep });
        Assert.Equal(HttpStatusCode.BadRequest, closed.StatusCode);
        Assert.Equal(0, await ProgressCount("SELECT count(*) FROM progress.validation"));

        await Link(subject, "person-multi", "active");
        var missing = await LearningSend(learning, HttpMethod.Post, "/validations", managerBearer, "people_manager", new { subjectId = subject, stepId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(0, await ProgressCount("SELECT count(*) FROM progress.validation"));
        Assert.Equal(0, await ProgressCount("SELECT count(*) FROM progress.audit_record WHERE action = 'validation.recorded'"));

        var validated = await LearningSend(learning, HttpMethod.Post, "/validations", managerBearer, "people_manager", new { subjectId = subject, stepId = gapStep });
        var validatedBody = await validated.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
        Assert.Contains("person-multi", validatedBody, StringComparison.Ordinal);
        Assert.Equal("completed", await ProgressText("SELECT status FROM progress.step WHERE step_id = $1", gapStep));
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.audit_record WHERE action = 'validation.recorded'"));

        var repeated = await LearningSend(learning, HttpMethod.Post, "/validations", managerBearer, "people_manager", new { subjectId = subject, stepId = gapStep });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.audit_record WHERE action = 'validation.recorded'"));

        await Link(subject, "other-manager", "active");
        var otherBearer = await _infrastructure.IssueTokenAsync("other-manager", "LE-LOCAL");
        var kept = await LearningSend(learning, HttpMethod.Post, "/validations", otherBearer, "people_manager", new { subjectId = subject, stepId = gapStep });
        var keptBody = await kept.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Contains("person-multi", keptBody, StringComparison.Ordinal);
        Assert.Equal("person-multi", await ProgressText("SELECT manager_employee_id FROM progress.validation WHERE step_id = $1", gapStep));
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.audit_record WHERE action = 'validation.recorded'"));

        var catalog = _infrastructure.Learning.Services.GetRequiredService<Learning.IExternalCatalog>();
        var before = await ProgressCount("SELECT count(*) FROM progress.step WHERE path_id = $1", pathId);
        var completed = await LearningSend(learning, HttpMethod.Post, $"/progress/steps/{laterStep}/completions", bearer, "employee", null);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        Assert.Equal(before, await ProgressCount("SELECT count(*) FROM progress.step WHERE path_id = $1", pathId));
        Assert.Equal("completed", await ProgressText("SELECT status FROM progress.step WHERE step_id = $1", laterStep));
        Assert.Equal(0, catalog.Calls);
        var assessmentStep = Guid.NewGuid();
        await ProgressExecute(
            """
            INSERT INTO progress.step (step_id, subject_id, provider, title, status, recorded_at)
            VALUES ($1, $2, 'skillhorizon_assessment', 'SkillHorizon assessment', 'completed', now())
            """,
            assessmentStep,
            subject);
        var internalCompletion = await LearningSend(learning, HttpMethod.Post, $"/progress/steps/{assessmentStep}/completions", bearer, "employee", null);
        Assert.Equal(HttpStatusCode.BadRequest, internalCompletion.StatusCode);
        Assert.Equal("completed", await ProgressText("SELECT status FROM progress.step WHERE step_id = $1", assessmentStep));
        Assert.Equal(0, catalog.Calls);

        var secondDraft = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId,
        [
            new TargetDraft(met.SkillId, 1),
            new TargetDraft(gap.SkillId, 1),
            new TargetDraft(later.SkillId, 1)
        ]), CancellationToken.None);
        var second = await benchmarks.PublishAsync(secondDraft.BenchmarkVersionId, "person-ld", "path-benchmark-next-" + _suffix, CancellationToken.None);
        var current = await LearningSend(learning, HttpMethod.Post, "/paths", bearer, "employee", new { });
        var currentBody = JsonSerializer.Deserialize<JsonElement>(await current.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(second.BenchmarkVersionId, currentBody.GetProperty("benchmarkVersionId").GetGuid());
        Assert.Equal(0, currentBody.GetProperty("steps").GetArrayLength());
        Assert.Equal(2, await ProgressCount(
            "SELECT count(*) FROM progress.path WHERE subject_id = $1 AND benchmark_version_id IN ($2, $3)",
            subject,
            published.BenchmarkVersionId,
            second.BenchmarkVersionId));
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task Link(string reportId, string managerId, string status)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var person = new NpgsqlCommand(
            """
            INSERT INTO workforce.person (
                employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                personal_learning_assigned, display_name, closed_at, projected_at)
            VALUES ($1, 'LE-LOCAL', 'IN', $2, $3, true, $1, CASE WHEN $3 = 'closed' THEN now() ELSE NULL END, now())
            ON CONFLICT (employee_id) DO UPDATE
                SET manager_employee_id = EXCLUDED.manager_employee_id,
                    employment_status = EXCLUDED.employment_status,
                    closed_at = EXCLUDED.closed_at
            """,
            connection);
        person.Parameters.AddWithValue(reportId);
        person.Parameters.AddWithValue(managerId);
        person.Parameters.AddWithValue(status);
        await person.ExecuteNonQueryAsync();
        await using var link = new NpgsqlCommand(
            """
            INSERT INTO workforce.management_relationship (manager_employee_id, report_employee_id)
            VALUES ($1, $2)
            ON CONFLICT DO NOTHING
            """,
            connection);
        link.Parameters.AddWithValue(managerId);
        link.Parameters.AddWithValue(reportId);
        await link.ExecuteNonQueryAsync();
        if (managerId != "person-multi")
        {
            await using var remove = new NpgsqlCommand(
                "DELETE FROM workforce.management_relationship WHERE report_employee_id = $1 AND manager_employee_id <> $2",
                connection);
            remove.Parameters.AddWithValue(reportId);
            remove.Parameters.AddWithValue(managerId);
            await remove.ExecuteNonQueryAsync();
        }
    }

    private async Task Grant(string subjectId, string workspace)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
            VALUES ($1, $2, $3, now())
            ON CONFLICT (subject_id, workspace) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(workspace);
        await command.ExecuteNonQueryAsync();
    }

    private async Task Cleanup(string subjectId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM workforce.management_relationship WHERE report_employee_id IN ($1, 'path-leaver')",
            "DELETE FROM workforce.person WHERE employee_id IN ($1, 'path-leaver')",
            """
            DELETE FROM access.workspace_grant
            WHERE (subject_id = $1 AND workspace = 'employee')
               OR (subject_id = 'other-manager' AND workspace = 'people_manager')
            """
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(subjectId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, subjectId == "other-manager" ? "LE-LOCAL" : "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "path-" + Guid.NewGuid().ToString("N"));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LearningKey(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/results");
        request.Headers.Add("X-Learning-Key", FrameworkInfrastructure.LearningKey);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LearningSend(HttpClient client, HttpMethod method, string path, string bearer, string workspace, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Add("X-Active-Workspace", workspace);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task ClearDrafts()
    {
        await Execute("DELETE FROM benchmark.target WHERE benchmark_version_id IN (SELECT benchmark_version_id FROM benchmark.version WHERE status = 'draft')");
        await Execute("DELETE FROM benchmark.version WHERE status = 'draft'");
        await Execute("DELETE FROM taxonomy.version_skill WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')");
        await Execute("DELETE FROM taxonomy.proficiency WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')");
        await Execute("DELETE FROM taxonomy.version WHERE status = 'draft'");
    }

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ProgressCount(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.ProgressConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task ProgressExecute(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.ProgressConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> ProgressText(string sql, Guid? id = null)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.ProgressConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is Guid value)
            command.Parameters.AddWithValue(value);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> Scan(string connectionString, string needle)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var columns = new List<(string Schema, string Table, string Column)>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT table_schema, table_name, column_name
            FROM information_schema.columns
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
              AND data_type IN ('text', 'jsonb', 'json', 'character varying')
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        var total = 0;
        foreach (var (schema, table, column) in columns)
        {
            await using var command = new NpgsqlCommand(
                $"SELECT count(*) FROM {Quote(schema)}.{Quote(table)} WHERE {Quote(column)}::text LIKE $1",
                connection);
            command.Parameters.AddWithValue("%" + needle + "%");
            total += Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        return total;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
