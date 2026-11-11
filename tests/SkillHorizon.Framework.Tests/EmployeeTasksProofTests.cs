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

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class EmployeeTasksProofTests
{
    private const string Prompt = "task-item-prompt";
    private const string Choice = "task-choice-label";

    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public EmployeeTasksProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Closing_a_task_writes_evidence_and_leaves_the_sealed_result()
    {
        await ClearDrafts();
        await ExecuteAssessment("UPDATE assessment.sitting SET status = 'sealed' WHERE subject_id = 'person-single' AND status = 'open'");

        using var api = _infrastructure.Api.CreateClient();
        var budget = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 4 });
        Assert.Equal(HttpStatusCode.NoContent, budget.StatusCode);

        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        await jobs.ImportAsync(
            [
                new FrameworkImport(
                    Code("field"),
                    "Field",
                    [new LevelImport(Code("apprentice"), "Apprentice", 1)],
                    [new ProfileImport(Code("analyst"), "Task analyst", Code("apprentice"))])
            ],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [
                    new PracticeDraft(Code("practice"), "Task practice",
                    [
                        new CompetencyDraft(Code("competency"), "Task competency",
                        [
                            new SkillDraft(Code("gap"), "Task measure")
                        ])
                    ])
                ],
                [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]),
            CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "taxonomy-" + _suffix, CancellationToken.None);
        var skill = publishedTaxonomy.Skills.Single();
        var draftBenchmark = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 2)]),
            CancellationToken.None);
        var published = await benchmarks.PublishAsync(draftBenchmark.BenchmarkVersionId, "person-ld", "benchmark-" + _suffix, CancellationToken.None);
        await InsertItem(skill.Code);

        var admitted = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
        {
            benchmarkVersionId = published.BenchmarkVersionId,
            subjectIds = new[] { "person-single" }
        });
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        var waveId = JsonSerializer.Deserialize<JsonElement>(await admitted.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();
        var authorized = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", "person-single", "employee", new { waveId });
        var grant = JsonSerializer.Deserialize<JsonElement>(await authorized.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        using var learning = _infrastructure.Learning.CreateClient();
        var opened = await LearningSend(learning, HttpMethod.Post, "/sittings", grant.GetProperty("token").GetString()!, null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var sitting = JsonSerializer.Deserialize<JsonElement>(await opened.Content.ReadAsStringAsync());
        var answered = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.GetProperty("sittingId").GetGuid()}/responses", grant.GetProperty("token").GetString()!, new
        {
            itemId = sitting.GetProperty("item").GetProperty("itemId").GetGuid(),
            rank = 1
        });
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var sealedSitting = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.GetProperty("sittingId").GetGuid()}/seal", grant.GetProperty("token").GetString()!, null);
        Assert.Equal(HttpStatusCode.OK, sealedSitting.StatusCode);
        var sealedResults = await Count("SELECT count(*) FROM readiness.sealed_result WHERE subject_id = 'person-single'");
        var proficiencies = await Count("SELECT count(*) FROM readiness.proficiency WHERE result_id IN (SELECT result_id FROM readiness.sealed_result WHERE subject_id = 'person-single')");

        using var employee = WebClient();
        var shell = await SignIn(employee, "person-single");
        var requestToken = shell.GetProperty("requestToken").GetString()!;
        var openedPath = await Post(employee, "/api/paths", new { }, requestToken);
        Assert.Equal(HttpStatusCode.OK, openedPath.StatusCode);

        var tasks = await Read(employee, "/api/tasks");
        Assert.DoesNotContain(Prompt, tasks.GetRawText(), StringComparison.Ordinal);
        var row = tasks.GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal("Task measure", row.GetProperty("title").GetString());
        Assert.Equal("assigned", row.GetProperty("status").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(28), DateOnly.Parse(row.GetProperty("dueOn").GetString()!));
        var stepId = row.GetProperty("stepId").GetGuid();
        var task = await Read(employee, "/api/tasks/" + stepId);
        Assert.Equal(stepId, task.GetProperty("stepId").GetGuid());

        var fileName = "hand-in-" + _suffix + ".txt";
        var closed = await Post(employee, "/api/tasks/" + stepId + "/close", new
        {
            stepId,
            fileName,
            contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("handed-in-" + _suffix))
        }, requestToken);
        var closedBody = await closed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var closedTask = JsonSerializer.Deserialize<JsonElement>(closedBody);
        Assert.Equal("completed", closedTask.GetProperty("status").GetString());
        Assert.Equal(fileName, closedTask.GetProperty("evidenceName").GetString());
        Assert.DoesNotContain(Prompt, closedBody, StringComparison.Ordinal);

        var remaining = await Read(employee, "/api/tasks");
        Assert.Empty(remaining.GetProperty("tasks").EnumerateArray());
        Assert.Equal(sealedResults, await Count("SELECT count(*) FROM readiness.sealed_result WHERE subject_id = 'person-single'"));
        Assert.Equal(proficiencies, await Count("SELECT count(*) FROM readiness.proficiency WHERE result_id IN (SELECT result_id FROM readiness.sealed_result WHERE subject_id = 'person-single')"));
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.step WHERE step_id = $1 AND status = 'completed'", stepId));
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.evidence WHERE step_id = $1 AND file_name = $2", stepId, fileName));

        using var other = WebClient();
        var entry = await SignIn(other, "person-multi");
        var chosen = await Post(other, "/api/workspace", new { workspace = "employee" }, entry.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/tasks?subject=person-single")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/tasks/" + stepId)).StatusCode);
        var refusedClose = await Post(other, "/api/tasks/" + stepId + "/close", new
        {
            fileName = "other.txt",
            contentBase64 = Convert.ToBase64String("nope"u8.ToArray())
        }, entry.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, refusedClose.StatusCode);

        using var manager = WebClient();
        var managerEntry = await SignIn(manager, "person-multi");
        var managerWorkspace = await Post(manager, "/api/workspace", new { workspace = "people_manager" }, managerEntry.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, managerWorkspace.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/tasks")).StatusCode);
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task InsertItem(string skillCode)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        var itemId = Guid.NewGuid();
        await using (var item = new NpgsqlCommand("INSERT INTO assessment.item (item_id, skill_code, prompt) VALUES ($1, $2, $3)", connection))
        {
            item.Parameters.AddWithValue(itemId);
            item.Parameters.AddWithValue(skillCode);
            item.Parameters.AddWithValue(Prompt);
            await item.ExecuteNonQueryAsync();
        }

        await using var choice = new NpgsqlCommand("INSERT INTO assessment.choice (item_id, rank, label) VALUES ($1, 1, $2), ($1, 2, 'Later')", connection);
        choice.Parameters.AddWithValue(itemId);
        choice.Parameters.AddWithValue(Choice);
        await choice.ExecuteNonQueryAsync();
    }

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "tasks-" + Guid.NewGuid().ToString("N"));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LearningSend(HttpClient client, HttpMethod method, string path, string sittingToken, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Sitting-Token", sittingToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private HttpClient WebClient() =>
        _infrastructure.Web.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<JsonElement> SignIn(HttpClient client, string subjectId)
    {
        var shell = await Read(client, "/api/shell");
        var response = await Post(client, "/api/signin", new { subjectId }, shell.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return await Read(client, "/api/shell");
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("RequestVerificationToken", token);
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

    private async Task ExecuteAssessment(string sql)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> Count(string sql)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> ProgressCount(string sql, Guid stepId, string? fileName = null)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.ProgressConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(stepId);
        if (fileName is not null)
            command.Parameters.AddWithValue(fileName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
