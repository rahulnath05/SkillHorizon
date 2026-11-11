using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Taxonomy;
using SkillHorizon.Workforce;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class ManagerWorkspaceProofTests
{
    private const string Prompt = "manager-item-prompt";
    private const string Choice = "manager-choice-label";

    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public ManagerWorkspaceProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task The_manager_workspace_reads_the_snapshot_and_leaves_the_benchmark()
    {
        await ClearDrafts();
        await ExecuteAssessment("UPDATE assessment.sitting SET status = 'sealed' WHERE subject_id = 'asha-rao' AND status = 'open'");
        using var api = _infrastructure.Api.CreateClient();
        var sync = _infrastructure.Api.Services.GetRequiredService<IWorkforceSync>();
        var feed = _infrastructure.Api.Services.GetRequiredService<IHrFeed>();
        await sync.ApplyAsync(await feed.ReadAsync(CancellationToken.None), CancellationToken.None);

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
                    [new ProfileImport(Code("analyst"), "Manager analyst", Code("apprentice"))])
            ],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [
                    new PracticeDraft(Code("practice"), "Manager practice",
                    [
                        new CompetencyDraft(Code("competency"), "Manager competency",
                        [new SkillDraft(Code("gap"), "Manager measure")])
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
            subjectIds = new[] { "asha-rao" }
        });
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        var waveId = JsonSerializer.Deserialize<JsonElement>(await admitted.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();
        var authorized = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", "asha-rao", "employee", new { waveId });
        var grant = JsonSerializer.Deserialize<JsonElement>(await authorized.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var token = grant.GetProperty("token").GetString()!;
        using var learning = _infrastructure.Learning.CreateClient();
        var opened = await LearningSend(learning, HttpMethod.Post, "/sittings", token, null, null, null);
        var sitting = JsonSerializer.Deserialize<JsonElement>(await opened.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var answered = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.GetProperty("sittingId").GetGuid()}/responses", token, new
        {
            itemId = sitting.GetProperty("item").GetProperty("itemId").GetGuid(),
            rank = 1
        }, null, null);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var sealedSitting = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.GetProperty("sittingId").GetGuid()}/seal", token, null, null, null);
        Assert.Equal(HttpStatusCode.OK, sealedSitting.StatusCode);
        var ashaBearer = await _infrastructure.IssueTokenAsync("asha-rao", "LE-LOCAL");
        var openedPath = await LearningSend(learning, HttpMethod.Post, "/paths", null, null, ashaBearer, "employee");
        var pathBody = await openedPath.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, openedPath.StatusCode);
        var stepId = JsonSerializer.Deserialize<JsonElement>(pathBody).GetProperty("steps").EnumerateArray().Single().GetProperty("stepId").GetGuid();
        var evidence = await LearningSend(learning, HttpMethod.Post, "/paths/evidence", null, new
        {
            stepId,
            fileName = "note-" + _suffix + ".txt",
            contentBase64 = Convert.ToBase64String("observed"u8.ToArray())
        }, ashaBearer, "employee");
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);

        var sealedResults = await Count("SELECT count(*) FROM readiness.sealed_result WHERE subject_id = 'asha-rao'");
        var proficiencies = await Count("SELECT count(*) FROM readiness.proficiency WHERE result_id IN (SELECT result_id FROM readiness.sealed_result WHERE subject_id = 'asha-rao')");
        var benchmarkRows = await Count("SELECT count(*) FROM benchmark.version");
        await _infrastructure.Api.Services.GetRequiredService<ITeamWorkspace>().ProjectAsync(CancellationToken.None);

        using var manager = WebClient();
        var entry = await SignIn(manager, "person-multi");
        var requestToken = entry.GetProperty("requestToken").GetString()!;
        var chosen = await Post(manager, "/api/workspace", new { workspace = "people_manager" }, requestToken);
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);

        var snapshot = await Read(manager, "/api/team/snapshot");
        var body = snapshot.GetProperty("snapshot");
        Assert.Equal(2, body.GetProperty("reportCount").GetInt32());
        Assert.Equal(1, body.GetProperty("attentionCount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("asOf").GetString()));
        var members = body.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(2, members.Count);
        Assert.Contains(members, member => member.GetProperty("employeeId").GetString() == "asha-rao" && member.GetProperty("position").GetString() == "gap" && member.GetProperty("attention").GetBoolean());
        Assert.Contains(members, member => member.GetProperty("employeeId").GetString() == "dev-kapoor" && member.GetProperty("position").GetString() == "not-measured" && !member.GetProperty("attention").GetBoolean());
        Assert.DoesNotContain(members, member => member.GetProperty("employeeId").GetString() is "nisha-iyer" or "person-single" or "other-report");
        Assert.DoesNotContain(Prompt, snapshot.GetRawText(), StringComparison.Ordinal);

        var skills = await Read(manager, "/api/team/skills");
        var asha = skills.GetProperty("people").EnumerateArray().Single(person => person.GetProperty("employeeId").GetString() == "asha-rao");
        Assert.Contains(asha.GetProperty("skills").EnumerateArray(), row => row.GetProperty("skillName").GetString() == "Manager measure" && row.GetProperty("currentRank").GetInt32() == 1 && row.GetProperty("targetRank").GetInt32() == 2);
        Assert.DoesNotContain(Prompt, skills.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, skills.GetRawText(), StringComparison.Ordinal);

        var reviews = await Read(manager, "/api/team/reviews");
        Assert.Contains(reviews.GetProperty("profiles").EnumerateArray(), row => row.GetProperty("employeeId").GetString() == "asha-rao");
        Assert.Contains(reviews.GetProperty("assessments").EnumerateArray(), row => row.GetProperty("employeeId").GetString() == "asha-rao");
        var confirmed = await Post(manager, "/api/team/profile-confirmations", new { subjectId = "asha-rao" }, requestToken);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        var reviewed = await Post(manager, "/api/team/assessment-reviews", new { subjectId = "asha-rao", note = "Context reviewed." }, requestToken);
        Assert.Equal(HttpStatusCode.NoContent, reviewed.StatusCode);
        var afterReviews = await Read(manager, "/api/team/reviews");
        Assert.DoesNotContain(afterReviews.GetProperty("profiles").EnumerateArray(), row => row.GetProperty("employeeId").GetString() == "asha-rao");
        Assert.DoesNotContain(afterReviews.GetProperty("assessments").EnumerateArray(), row => row.GetProperty("employeeId").GetString() == "asha-rao");
        Assert.Equal(proficiencies, await Count("SELECT count(*) FROM readiness.proficiency WHERE result_id IN (SELECT result_id FROM readiness.sealed_result WHERE subject_id = 'asha-rao')"));

        var pending = await Read(manager, "/api/team/evidence");
        Assert.Contains(pending.GetProperty("evidence").EnumerateArray(), row => row.GetProperty("stepId").GetGuid() == stepId);
        var validated = await Post(manager, "/api/validations", new { subjectId = "asha-rao", stepId }, requestToken);
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
        Assert.Equal("person-multi", await ProgressText("SELECT manager_employee_id FROM progress.validation WHERE step_id = $1", stepId));
        Assert.Equal("asha-rao", await ProgressText("SELECT report_employee_id FROM progress.validation WHERE step_id = $1", stepId));
        var cleared = await Read(manager, "/api/team/evidence");
        Assert.DoesNotContain(cleared.GetProperty("evidence").EnumerateArray(), row => row.GetProperty("stepId").GetGuid() == stepId);
        Assert.Equal(sealedResults, await Count("SELECT count(*) FROM readiness.sealed_result WHERE subject_id = 'asha-rao'"));

        var commitment = await Post(manager, "/api/team/commitments", new { subjectId = "asha-rao", title = "Protect the lab", dueOn = "2026-12-01" }, requestToken);
        Assert.Equal(HttpStatusCode.OK, commitment.StatusCode);
        var listed = await Read(manager, "/api/team/commitments");
        Assert.Contains(listed.GetProperty("commitments").EnumerateArray(), row => row.GetProperty("employeeId").GetString() == "asha-rao" && row.GetProperty("title").GetString() == "Protect the lab");
        var outsiderCommitment = await Post(manager, "/api/team/commitments", new { subjectId = "person-single", title = "Not a report", dueOn = "2026-12-01" }, requestToken);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderCommitment.StatusCode);

        var correction = await Post(manager, "/api/team/corrections", new { subjectId = "asha-rao", note = "The profile should name the reporting work." }, requestToken);
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        Assert.Equal(1, await CountText("SELECT count(*) FROM workforce.profile_correction WHERE report_employee_id = 'asha-rao' AND note = $1", "The profile should name the reporting work."));
        Assert.Equal(benchmarkRows, await Count("SELECT count(*) FROM benchmark.version"));
        var outsiderCorrection = await Post(manager, "/api/team/corrections", new { subjectId = "person-single", note = "Not a report" }, requestToken);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderCorrection.StatusCode);

        using var employee = WebClient();
        await SignIn(employee, "person-single");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/team/snapshot")).StatusCode);
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
        request.Headers.Add("X-Correlation-Id", "manager-" + Guid.NewGuid().ToString("N"));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LearningSend(HttpClient client, HttpMethod method, string path, string? sittingToken, object? body, string? bearer, string? workspace)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(sittingToken))
            request.Headers.Add("X-Sitting-Token", sittingToken);
        if (!string.IsNullOrWhiteSpace(bearer))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (!string.IsNullOrWhiteSpace(workspace))
            request.Headers.Add("X-Active-Workspace", workspace);
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

    private async Task<int> CountText(string sql, string text)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(text);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string> ProgressText(string sql, Guid stepId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.ProgressConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(stepId);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
