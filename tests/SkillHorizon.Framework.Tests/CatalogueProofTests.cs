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
public sealed class CatalogueProofTests
{
    private const string Prompt = "catalogue-item-prompt";
    private const string Choice = "catalogue-choice-label";

    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public CatalogueProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task The_catalogue_keeps_drafts_in_the_authoring_zone()
    {
        await ClearDrafts();
        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        var reference = _infrastructure.Api.Services.GetRequiredService<IReferenceLibrary>();
        await jobs.ImportAsync(
            [
                new FrameworkImport(
                    Code("field"),
                    "Catalogue field",
                    [new LevelImport(Code("associate"), "Associate", 1)],
                    [
                        new ProfileImport(Code("analyst"), "Catalogue analyst", Code("associate")),
                        new ProfileImport(Code("steward"), "Catalogue steward", Code("associate"))
                    ])
            ],
            CancellationToken.None);
        var profiles = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles;
        var analyst = profiles.Single(profile => profile.Code == Code("analyst"));
        var steward = profiles.Single(profile => profile.Code == Code("steward"));
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [new PracticeDraft(Code("practice"), "Catalogue practice", [new CompetencyDraft(Code("competency"), "Catalogue competency", [new SkillDraft(Code("published"), "Catalogue published")])])],
                [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]),
            CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "catalogue-taxonomy-" + _suffix, CancellationToken.None);
        await reference.ApplyPendingAsync(CancellationToken.None);
        var publishedSkill = publishedTaxonomy.Skills.Single();
        var edited = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [new PracticeDraft(Code("practice"), "Catalogue practice", [new CompetencyDraft(Code("competency"), "Catalogue competency", [new SkillDraft(Code("published"), "Catalogue published"), new SkillDraft(Code("draft"), "Catalogue draft")])])],
                [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]),
            CancellationToken.None);

        using var author = WebClient();
        await SignIn(author, "person-ld");
        var browser = await Read(author, "/api/catalogue/taxonomy");
        Assert.Equal("authoring", browser.GetProperty("source").GetString());
        Assert.Equal("draft", browser.GetProperty("version").GetProperty("status").GetString());
        Assert.Equal(edited.TaxonomyVersionId, browser.GetProperty("version").GetProperty("taxonomyVersionId").GetGuid());
        var competency = browser.GetProperty("version").GetProperty("competencies").EnumerateArray().Single();
        Assert.Equal("Catalogue competency", competency.GetProperty("name").GetString());
        Assert.Contains(competency.GetProperty("skills").EnumerateArray(), skill => skill.GetProperty("name").GetString() == "Catalogue draft");

        using var employee = WebClient();
        await SignIn(employee, "person-single");
        var subscriber = await Read(employee, "/api/catalogue/taxonomy");
        Assert.Equal("reference", subscriber.GetProperty("source").GetString());
        Assert.DoesNotContain("Catalogue draft", subscriber.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Code("draft"), subscriber.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(1, await CountGuid("SELECT count(*) FROM reference.taxonomy_skill WHERE taxonomy_version_id = $1 AND skill_name = 'Catalogue published'", publishedTaxonomy.TaxonomyVersionId));
        Assert.Equal(0, await CountText("SELECT count(*) FROM reference.taxonomy_skill WHERE skill_code = $1", Code("draft")));
        Assert.Equal(0, await CountText("SELECT count(*) FROM messaging.outbox WHERE message_type = 'taxonomy.published' AND payload::text LIKE $1", "%" + Code("draft") + "%"));

        var publishedBenchmark = await benchmarks.PublishAsync(
            (await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(analyst.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(publishedSkill.SkillId, 2)]), CancellationToken.None)).BenchmarkVersionId,
            "person-ld",
            "catalogue-benchmark-" + _suffix,
            CancellationToken.None);
        Assert.Equal(2, await Rank(analyst.ProfileId, Code("published")));
        var saved = await Post(author, "/api/benchmarks/draft", new
        {
            jobProfileId = analyst.ProfileId,
            taxonomyVersionId = publishedTaxonomy.TaxonomyVersionId,
            targets = new[] { new { skillId = publishedSkill.SkillId, proficiencyRank = 1 } }
        }, (await Read(author, "/api/shell")).GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(2, await Rank(analyst.ProfileId, Code("published")));
        var shell = await Read(author, "/api/shell");
        var requestToken = shell.GetProperty("requestToken").GetString()!;
        var workspace = await Read(author, "/api/catalogue/benchmark?profileId=" + analyst.ProfileId);
        var draftId = workspace.GetProperty("benchmarkVersionId").GetGuid();
        var published = await Post(author, "/api/benchmarks/publish", new { benchmarkVersionId = draftId }, requestToken);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(1, await Rank(analyst.ProfileId, Code("published")));
        Assert.NotEqual(publishedBenchmark.BenchmarkVersionId, await PublishedVersion(analyst.ProfileId));

        var sync = _infrastructure.Api.Services.GetRequiredService<IWorkforceSync>();
        var feed = _infrastructure.Api.Services.GetRequiredService<IHrFeed>();
        await sync.ApplyAsync(await feed.ReadAsync(CancellationToken.None), CancellationToken.None);
        using var api = _infrastructure.Api.CreateClient();
        var filed = await Send(api, HttpMethod.Post, "/workforce/team/corrections", "person-multi", "people_manager", new { subjectId = "asha-rao", note = "Catalogue profile " + _suffix });
        Assert.Equal(HttpStatusCode.OK, filed.StatusCode);
        var requestId = JsonSerializer.Deserialize<JsonElement>(await filed.Content.ReadAsStringAsync()).GetProperty("requestId").GetGuid();
        var beforeOpen = await Read(author, "/api/catalogue/overview");
        var publishedRows = await Count("SELECT count(*) FROM benchmark.version WHERE status = 'published'");
        var opened = await Post(author, $"/api/catalogue/requests/{requestId}/open", new
        {
            profileName = "Opened " + _suffix,
            frameworkCode = Code("field"),
            levelCode = Code("associate")
        }, requestToken);
        var openedBody = await opened.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var openedProfile = JsonSerializer.Deserialize<JsonElement>(openedBody).GetProperty("profile").GetProperty("profileId").GetGuid();
        Assert.False(JsonSerializer.Deserialize<JsonElement>(openedBody).GetProperty("published").GetBoolean());
        Assert.Equal(0, await CountGuid("SELECT count(*) FROM benchmark.version WHERE job_profile_id = $1", openedProfile));
        Assert.Equal(publishedRows, await Count("SELECT count(*) FROM benchmark.version WHERE status = 'published'"));
        var requests = await Read(author, "/api/catalogue/requests");
        Assert.Contains(requests.GetProperty("requests").EnumerateArray(), row => row.GetProperty("requestId").GetGuid() == requestId && row.GetProperty("profileName").GetString() == "Opened " + _suffix);
        Assert.Equal(beforeOpen.GetProperty("requestCount").GetInt32() - 1, (await Read(author, "/api/catalogue/overview")).GetProperty("requestCount").GetInt32());

        var unsent = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(steward.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(publishedSkill.SkillId, 1)]), CancellationToken.None);
        var sent = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(analyst.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(publishedSkill.SkillId, 1)]), CancellationToken.None);
        var beforeSend = (await Read(author, "/api/catalogue/overview")).GetProperty("approvalCount").GetInt32();
        var submission = await Post(author, "/api/catalogue/submissions", new { benchmarkVersionId = sent.BenchmarkVersionId }, requestToken);
        Assert.Equal(HttpStatusCode.OK, submission.StatusCode);
        var approvals = await Read(author, "/api/catalogue/approvals");
        Assert.Contains(approvals.GetProperty("drafts").EnumerateArray(), row => row.GetProperty("benchmarkVersionId").GetGuid() == sent.BenchmarkVersionId);
        Assert.DoesNotContain(approvals.GetProperty("drafts").EnumerateArray(), row => row.GetProperty("benchmarkVersionId").GetGuid() == unsent.BenchmarkVersionId);
        await Execute($"INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at) VALUES ('{Guid.NewGuid()}', 'person-lead', 'competency_lead', now()) ON CONFLICT (subject_id, workspace) DO NOTHING");
        var review = await Send(api, HttpMethod.Get, "/catalogue/review", "person-lead", "competency_lead", null);
        var reviewBody = await review.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains(sent.BenchmarkVersionId.ToString(), reviewBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unsent.BenchmarkVersionId.ToString(), reviewBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, HttpMethod.Get, "/catalogue/review", "person-single", "employee", null)).StatusCode);
        Assert.Equal(beforeSend + 1, (await Read(author, "/api/catalogue/overview")).GetProperty("approvalCount").GetInt32());

        await Execute(
            """
            INSERT INTO readiness.snapshot (snapshot_id, subject_kind, subject_key, as_of, dirty, member_count, measured_count, gap_count, cohort_count)
            VALUES ($1, 'practice', $2, now(), false, 4, 3, 1, 0)
            ON CONFLICT (subject_kind, subject_key) DO UPDATE
                SET member_count = 4, measured_count = 3, gap_count = 1, cohort_count = 0, dirty = false, as_of = now()
            """,
            Guid.NewGuid(),
            "catalogue-" + _suffix);
        await ExecuteAssessment(Prompt);
        var overview = await Read(author, "/api/catalogue/overview");
        Assert.Contains(overview.GetProperty("snapshots").EnumerateArray(), row => row.GetProperty("practiceCode").GetString() == "catalogue-" + _suffix && row.GetProperty("memberCount").GetInt32() == 4 && row.GetProperty("gapCount").GetInt32() == 1);
        Assert.DoesNotContain(Prompt, overview.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, overview.GetRawText(), StringComparison.Ordinal);
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task<int> Rank(Guid profileId, string skillCode)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT target.proficiency_rank
            FROM benchmark.target AS target
            JOIN benchmark.version AS version ON version.benchmark_version_id = target.benchmark_version_id
            WHERE version.job_profile_id = $1 AND version.status = 'published' AND target.skill_code = $2
            """,
            connection);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(skillCode);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<Guid> PublishedVersion(Guid profileId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT benchmark_version_id FROM benchmark.version WHERE job_profile_id = $1 AND status = 'published'", connection);
        command.Parameters.AddWithValue(profileId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "catalogue-" + Guid.NewGuid().ToString("N"));
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

    private static async Task SignIn(HttpClient client, string subjectId)
    {
        var shell = await Read(client, "/api/shell");
        var response = await Post(client, "/api/signin", new { subjectId }, shell.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await Read(client, "/api/shell");
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
        await Execute("DELETE FROM catalogue.submission WHERE benchmark_version_id IN (SELECT benchmark_version_id FROM benchmark.version WHERE status = 'draft')");
        await Execute("DELETE FROM benchmark.target WHERE benchmark_version_id IN (SELECT benchmark_version_id FROM benchmark.version WHERE status = 'draft')");
        await Execute("DELETE FROM benchmark.version WHERE status = 'draft'");
        await Execute("DELETE FROM taxonomy.version_skill WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')");
        await Execute("DELETE FROM taxonomy.proficiency WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')");
        await Execute("DELETE FROM taxonomy.version WHERE status = 'draft'");
    }

    private async Task Execute(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAssessment(string prompt)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        var itemId = Guid.NewGuid();
        await using (var item = new NpgsqlCommand("INSERT INTO assessment.item (item_id, skill_code, prompt) VALUES ($1, $2, $3)", connection))
        {
            item.Parameters.AddWithValue(itemId);
            item.Parameters.AddWithValue(Code("item"));
            item.Parameters.AddWithValue(prompt);
            await item.ExecuteNonQueryAsync();
        }

        await using var choice = new NpgsqlCommand("INSERT INTO assessment.choice (item_id, rank, label) VALUES ($1, 1, $2)", connection);
        choice.Parameters.AddWithValue(itemId);
        choice.Parameters.AddWithValue(Choice);
        await choice.ExecuteNonQueryAsync();
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

    private async Task<int> CountGuid(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
