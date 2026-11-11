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
public sealed class EmployeeHomeProofTests
{
    private const string Prompt = "home-item-prompt";
    private const string Choice = "home-choice-label";

    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public EmployeeHomeProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task The_employee_home_reads_only_the_signed_in_person()
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
                    [new ProfileImport(Code("analyst"), "Home analyst", Code("apprentice"))])
            ],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [
                    new PracticeDraft(Code("practice"), "Home practice",
                    [
                        new CompetencyDraft(Code("competency"), "Home competency",
                        [
                            new SkillDraft(Code("met"), "Met measure"),
                            new SkillDraft(Code("gap"), "Gap measure")
                        ])
                    ])
                ],
                [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]),
            CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "taxonomy-" + _suffix, CancellationToken.None);
        var met = publishedTaxonomy.Skills.Single(skill => skill.Code == Code("met"));
        var gap = publishedTaxonomy.Skills.Single(skill => skill.Code == Code("gap"));
        var draftBenchmark = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(met.SkillId, 2), new TargetDraft(gap.SkillId, 2)]),
            CancellationToken.None);
        var published = await benchmarks.PublishAsync(draftBenchmark.BenchmarkVersionId, "person-ld", "benchmark-" + _suffix, CancellationToken.None);
        await InsertItem(met.Code);
        await InsertItem(gap.Code);

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
        var token = grant.GetProperty("token").GetString()!;

        using var learning = _infrastructure.Learning.CreateClient();
        var opened = await LearningSend(learning, HttpMethod.Post, "/sittings", token, null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        using var employee = WebClient();
        var shell = await SignIn(employee, "person-single");
        var openHome = await Read(employee, "/api/home");
        Assert.True(openHome.GetProperty("sittingOpen").GetBoolean());
        Assert.DoesNotContain(Prompt, openHome.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, openHome.GetRawText(), StringComparison.Ordinal);

        var sitting = JsonSerializer.Deserialize<JsonElement>(await opened.Content.ReadAsStringAsync());
        while (!sitting.GetProperty("readyToSeal").GetBoolean())
        {
            var item = sitting.GetProperty("item");
            var rank = item.GetProperty("skillCode").GetString()!.Contains("met", StringComparison.Ordinal) ? 2 : 1;
            var answered = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.GetProperty("sittingId").GetGuid()}/responses", token, new
            {
                itemId = item.GetProperty("itemId").GetGuid(),
                rank
            });
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
            sitting = JsonSerializer.Deserialize<JsonElement>(await answered.Content.ReadAsStringAsync());
        }

        var sealedSitting = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.GetProperty("sittingId").GetGuid()}/seal", token, null);
        Assert.Equal(HttpStatusCode.OK, sealedSitting.StatusCode);

        var home = await Read(employee, "/api/home");
        Assert.Equal(1, home.GetProperty("atTarget").GetInt32());
        Assert.Equal(1, home.GetProperty("belowTarget").GetInt32());
        Assert.False(home.GetProperty("sittingOpen").GetBoolean());
        Assert.DoesNotContain(Prompt, home.GetRawText(), StringComparison.Ordinal);

        var skills = await Read(employee, "/api/skills");
        var rows = skills.GetProperty("skills").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.GetProperty("skillName").GetString() == "Met measure" && row.GetProperty("currentRank").GetInt32() == 2);
        Assert.Contains(rows, row => row.GetProperty("skillName").GetString() == "Gap measure" && row.GetProperty("currentRank").GetInt32() == 1 && row.GetProperty("targetRank").GetInt32() == 2);
        Assert.DoesNotContain(Prompt, skills.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, skills.GetRawText(), StringComparison.Ordinal);

        var requestToken = shell.GetProperty("requestToken").GetString()!;
        var openedPath = await Post(employee, "/api/paths", new { }, requestToken);
        var pathBody = await openedPath.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, openedPath.StatusCode);
        var path = JsonSerializer.Deserialize<JsonElement>(pathBody);
        var stepId = path.GetProperty("steps").EnumerateArray().Single(step => step.GetProperty("skillCode").GetString() == gap.Code).GetProperty("stepId").GetGuid();
        var fileName = "note-" + _suffix + ".txt";
        var fileBytes = Encoding.UTF8.GetBytes("own-evidence-" + _suffix);
        var withEvidence = await Post(employee, "/api/paths/evidence", new
        {
            stepId,
            fileName,
            contentBase64 = Convert.ToBase64String(fileBytes)
        }, requestToken);
        Assert.Equal(HttpStatusCode.OK, withEvidence.StatusCode);

        var afterPath = await Read(employee, "/api/home");
        Assert.Equal(1, afterPath.GetProperty("openSteps").GetInt32());

        var documents = await Read(employee, "/api/documents");
        var document = documents.GetProperty("documents").EnumerateArray().Single(row => row.GetProperty("fileName").GetString() == fileName);
        Assert.Equal(fileBytes.Length, document.GetProperty("byteLength").GetInt32());
        Assert.DoesNotContain("objectKey", documents.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Prompt, documents.GetRawText(), StringComparison.Ordinal);
        var download = await employee.GetAsync("/api/documents/" + document.GetProperty("evidenceId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(fileBytes, await download.Content.ReadAsByteArrayAsync());

        var direct = await Send(api, HttpMethod.Get, "/employee/skills?subject=person-single", "person-multi", "employee", null);
        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);

        using var other = WebClient();
        var entry = await SignIn(other, "person-multi");
        var chosen = await Post(other, "/api/workspace", new { workspace = "employee" }, entry.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/skills?subject=person-single")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/documents?subject=person-single")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/home?subject=person-single")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/documents/" + document.GetProperty("evidenceId").GetGuid())).StatusCode);

        using var manager = WebClient();
        var managerEntry = await SignIn(manager, "person-multi");
        var managerWorkspace = await Post(manager, "/api/workspace", new { workspace = "people_manager" }, managerEntry.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, managerWorkspace.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/skills")).StatusCode);
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
        request.Headers.Add("X-Correlation-Id", "home-" + Guid.NewGuid().ToString("N"));
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
}
