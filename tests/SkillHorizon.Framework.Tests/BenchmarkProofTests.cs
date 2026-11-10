using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Taxonomy;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class BenchmarkProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public BenchmarkProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Published_taxonomy_and_benchmark_are_immutable_and_drafts_stay_out_of_the_zone()
    {
        await ClearDrafts();
        var jobs = Jobs();
        var taxonomy = Taxonomy();
        var benchmarks = Benchmarks();
        var reference = Reference();

        await jobs.ImportAsync(
            [
                new FrameworkImport(
                    Code("field"),
                    "Field",
                    [new LevelImport("principal", "Principal", 2), new LevelImport("apprentice", "Apprentice", 1)],
                    [new ProfileImport(Code("analyst"), "Field analyst", "apprentice")]),
                new FrameworkImport(
                    Code("studio"),
                    "Studio",
                    [new LevelImport("maker", "Maker", 1), new LevelImport("lead", "Lead", 2)],
                    [new ProfileImport(Code("editor"), "Studio editor", "lead")])
            ],
            CancellationToken.None);

        var listed = await jobs.ListAsync(CancellationToken.None);
        var field = listed.Single(framework => framework.Code == Code("field"));
        var studio = listed.Single(framework => framework.Code == Code("studio"));
        Assert.Equal(["Apprentice", "Principal"], field.Levels.Select(level => level.Name).ToArray());
        Assert.Equal([1, 2], field.Levels.Select(level => level.Rank).ToArray());
        Assert.Equal(["Maker", "Lead"], studio.Levels.Select(level => level.Name).ToArray());
        Assert.Equal("Lead", studio.Profiles.Single().LevelName);

        var drafted = await taxonomy.SaveDraftAsync(TaxonomyBody("Field SQL"), CancellationToken.None);
        var profile = field.Profiles.Single();
        var skill = drafted.Skills.Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, drafted.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 1)]),
            CancellationToken.None));
        Assert.Equal(0, await Count("SELECT count(*) FROM benchmark.version WHERE job_profile_id = $1", profile.ProfileId));

        var pinned = Guid.NewGuid();
        var pinnedCorrelation = "pinned-" + _suffix;
        await InsertDraftBenchmark(pinned, profile.ProfileId, drafted.TaxonomyVersionId);
        var pinnedFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            benchmarks.PublishAsync(pinned, "person-ld", pinnedCorrelation, CancellationToken.None));
        Assert.Contains("published taxonomy", pinnedFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("draft", await ScalarString("SELECT status FROM benchmark.version WHERE benchmark_version_id = $1", pinned));
        Assert.Equal(0, await CountText("SELECT count(*) FROM audit.audit_record WHERE correlation_id = $1", pinnedCorrelation));
        Assert.Equal(0, await CountText("SELECT count(*) FROM messaging.outbox WHERE correlation_id = $1", pinnedCorrelation));
        await Execute("DELETE FROM benchmark.version WHERE benchmark_version_id = $1 AND status = 'draft'", pinned);

        var missingCorrelation = "missing-" + _suffix;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            benchmarks.PublishAsync(Guid.NewGuid(), "person-ld", missingCorrelation, CancellationToken.None));
        Assert.Equal(0, await CountText("SELECT count(*) FROM audit.audit_record WHERE correlation_id = $1", missingCorrelation));
        Assert.Equal(0, await CountText("SELECT count(*) FROM messaging.outbox WHERE correlation_id = $1", missingCorrelation));

        var firstTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "taxonomy-" + _suffix, CancellationToken.None);
        Assert.Equal("published", firstTaxonomy.Status);
        Assert.Equal("Field SQL", firstTaxonomy.Skills.Single().Name);

        var renamed = await taxonomy.SaveDraftAsync(TaxonomyBody("Field SQL renamed"), CancellationToken.None);
        var secondTaxonomy = await taxonomy.PublishAsync(renamed.TaxonomyVersionId, "person-ld", "taxonomy-next-" + _suffix, CancellationToken.None);
        var retained = await taxonomy.FindAsync(firstTaxonomy.TaxonomyVersionId, CancellationToken.None);
        Assert.NotNull(retained);
        Assert.Equal("superseded", retained.Status);
        Assert.Equal("Field SQL", retained.Skills.Single().Name);
        Assert.Equal(secondTaxonomy.TaxonomyVersionId, await ScalarGuid(
            "SELECT superseded_by FROM taxonomy.version WHERE taxonomy_version_id = $1",
            firstTaxonomy.TaxonomyVersionId));
        Assert.Equal(firstTaxonomy.Skills.Single().SkillId, secondTaxonomy.Skills.Single().SkillId);
        Assert.Equal("Field SQL renamed", secondTaxonomy.Skills.Single().Name);

        var foundation = secondTaxonomy.Proficiencies.Single(item => item.Code == Code("foundation"));
        var summit = secondTaxonomy.Proficiencies.Single(item => item.Code == Code("summit"));
        var publishedSkill = secondTaxonomy.Skills.Single();
        var first = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, secondTaxonomy.TaxonomyVersionId, [new TargetDraft(publishedSkill.SkillId, foundation.Rank)]),
            CancellationToken.None);
        var publishCorrelation = "benchmark-" + _suffix;
        var published = await benchmarks.PublishAsync(first.BenchmarkVersionId, "person-ld", publishCorrelation, CancellationToken.None);
        Assert.Equal("published", published.Status);
        Assert.Equal(foundation.Name, published.Targets.Single().ProficiencyName);
        Assert.Equal(1, await CountText("SELECT count(*) FROM audit.audit_record WHERE correlation_id = $1 AND record_id = $2", publishCorrelation, published.BenchmarkVersionId));
        Assert.Equal(1, await CountText("SELECT count(*) FROM messaging.outbox WHERE correlation_id = $1 AND message_type = 'benchmark.published'", publishCorrelation));

        var repeatCorrelation = "repeat-" + _suffix;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            benchmarks.PublishAsync(published.BenchmarkVersionId, "person-ld", repeatCorrelation, CancellationToken.None));
        Assert.Equal(1, await CountText("SELECT count(*) FROM audit.audit_record WHERE correlation_id = $1", publishCorrelation));
        Assert.Equal(0, await CountText("SELECT count(*) FROM audit.audit_record WHERE correlation_id = $1", repeatCorrelation));

        var immutable = await Assert.ThrowsAsync<PostgresException>(() => Execute(
            "UPDATE benchmark.target SET proficiency_name = 'Rewritten' WHERE benchmark_version_id = $1",
            published.BenchmarkVersionId));
        Assert.Contains("immutable", immutable.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(foundation.Name, await ScalarString(
            "SELECT proficiency_name FROM benchmark.target WHERE benchmark_version_id = $1",
            published.BenchmarkVersionId));

        var draft = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, secondTaxonomy.TaxonomyVersionId, [new TargetDraft(publishedSkill.SkillId, summit.Rank)]),
            CancellationToken.None);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(summit.Name, draft.Targets.Single().ProficiencyName);
        Assert.Equal(0, await CountText("SELECT count(*) FROM messaging.outbox WHERE message_type = 'benchmark.published' AND payload::text LIKE $1", "%" + draft.BenchmarkVersionId + "%"));

        Assert.Equal(0, await Count("SELECT count(*) FROM reference.benchmark_version WHERE job_profile_id = $1", profile.ProfileId));
        var applied = await reference.ApplyPendingAsync(CancellationToken.None);
        Assert.True(applied >= 2);
        var copied = (await reference.HistoryAsync(profile.ProfileId, CancellationToken.None)).Single();
        Assert.Equal(published.BenchmarkVersionId, copied.BenchmarkVersionId);
        Assert.Equal("published", copied.Status);
        Assert.Equal(foundation.Name, copied.Targets.Single().ProficiencyName);
        Assert.DoesNotContain(summit.Name, copied.Targets.Select(target => target.ProficiencyName));

        var historyBefore = await benchmarks.HistoryAsync(profile.ProfileId, CancellationToken.None);
        Assert.Equal([published.VersionNumber], historyBefore.Select(version => version.VersionNumber).ToArray());
        Assert.DoesNotContain(historyBefore, version => version.BenchmarkVersionId == draft.BenchmarkVersionId);

        await AssertDraftHidden(summit.Name, draft.BenchmarkVersionId);

        var secondCorrelation = "benchmark-next-" + _suffix;
        var next = await benchmarks.PublishAsync(draft.BenchmarkVersionId, "person-ld", secondCorrelation, CancellationToken.None);
        Assert.Equal("published", next.Status);
        Assert.Equal(published.BenchmarkVersionId, next.Supersedes);
        var authoringHistory = await benchmarks.HistoryAsync(profile.ProfileId, CancellationToken.None);
        Assert.Equal([published.VersionNumber, next.VersionNumber], authoringHistory.Select(version => version.VersionNumber).ToArray());
        Assert.Equal("superseded", authoringHistory[0].Status);
        Assert.Equal(foundation.Name, authoringHistory[0].Targets.Single().ProficiencyName);
        Assert.Equal("published", authoringHistory[1].Status);
        Assert.Equal(summit.Name, authoringHistory[1].Targets.Single().ProficiencyName);
        Assert.Equal(0, await Count("SELECT count(*) FROM reference.benchmark_version WHERE benchmark_version_id = $1", next.BenchmarkVersionId));

        await reference.ApplyPendingAsync(CancellationToken.None);
        var zoneHistory = await reference.HistoryAsync(profile.ProfileId, CancellationToken.None);
        Assert.Equal([published.VersionNumber, next.VersionNumber], zoneHistory.Select(version => version.VersionNumber).ToArray());
        Assert.Equal("superseded", zoneHistory[0].Status);
        Assert.Equal(foundation.Name, zoneHistory[0].Targets.Single().ProficiencyName);
        Assert.Equal(summit.Name, zoneHistory[1].Targets.Single().ProficiencyName);
        Assert.Equal("published", await ScalarString(
            "SELECT status FROM reference.benchmark_version WHERE benchmark_version_id = $1",
            next.BenchmarkVersionId));

        using var employee = WebClient();
        await SignIn(employee, "person-single");
        var employeeStandards = await ReadStandards(employee);
        Assert.Equal("reference", employeeStandards.GetProperty("source").GetString());
        Assert.Equal(0, employeeStandards.GetProperty("drafts").GetArrayLength());
        Assert.Contains(foundation.Name, employeeStandards.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(summit.Name, employeeStandards.GetRawText(), StringComparison.Ordinal);

        using var unmapped = WebClient();
        var refused = await SignIn(unmapped, "person-unmapped");
        Assert.Equal("refused", refused.GetProperty("status").GetString());
    }

    private TaxonomyDraft TaxonomyBody(string skillName) => new(
        [
            new PracticeDraft(
                Code("practice"),
                "Field practice",
                [new CompetencyDraft(Code("competency"), "Field competency", [new SkillDraft(Code("skill"), skillName)])])
        ],
        [
            new ProficiencyDraft(Code("foundation"), "Foundation " + _suffix, 1),
            new ProficiencyDraft(Code("summit"), "Summit " + _suffix, 2)
        ]);

    private async Task AssertDraftHidden(string draftName, Guid draftId)
    {
        using var api = _infrastructure.Api.CreateClient();
        var employeeDrafts = await Send(api, HttpMethod.Get, "/benchmarks/drafts", "person-single", "employee", null);
        var managerDrafts = await Send(api, HttpMethod.Get, "/benchmarks/drafts", "person-multi", "people_manager", null);
        var authorDrafts = await Send(api, HttpMethod.Get, "/benchmarks/drafts", "person-ld", "learning_and_development", null);
        Assert.Equal(HttpStatusCode.Forbidden, employeeDrafts.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, managerDrafts.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorDrafts.StatusCode);
        Assert.Contains(draftId.ToString(), await authorDrafts.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var employeeSave = await Send(api, HttpMethod.Post, "/commands/benchmarks/draft", "person-single", "employee", new { });
        var managerSave = await Send(api, HttpMethod.Post, "/commands/benchmarks/draft", "person-multi", "people_manager", new { });
        Assert.Equal(HttpStatusCode.Forbidden, employeeSave.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, managerSave.StatusCode);

        var employeeStandards = await Send(api, HttpMethod.Get, "/standards", "person-single", "employee", null);
        var managerStandards = await Send(api, HttpMethod.Get, "/standards", "person-multi", "people_manager", null);
        var authorStandards = await Send(api, HttpMethod.Get, "/standards", "person-ld", "learning_and_development", null);
        Assert.Equal(HttpStatusCode.OK, employeeStandards.StatusCode);
        var employeeBody = await employeeStandards.Content.ReadAsStringAsync();
        var managerBody = await managerStandards.Content.ReadAsStringAsync();
        var authorBody = await authorStandards.Content.ReadAsStringAsync();
        Assert.DoesNotContain(draftName, employeeBody, StringComparison.Ordinal);
        Assert.DoesNotContain(draftName, managerBody, StringComparison.Ordinal);
        Assert.Contains(draftName, authorBody, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"reference\"", employeeBody, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"authoring\"", authorBody, StringComparison.Ordinal);

        using var single = WebClient();
        await SignIn(single, "person-single");
        Assert.DoesNotContain(draftName, (await ReadStandards(single)).GetRawText(), StringComparison.Ordinal);

        using var learning = WebClient();
        await SignIn(learning, "person-ld");
        Assert.Contains(draftName, (await ReadStandards(learning)).GetRawText(), StringComparison.Ordinal);

        using var manager = WebClient();
        var entry = await SignIn(manager, "person-multi");
        var chosen = await Post(manager, "/api/workspace", new { workspace = "people_manager" }, entry.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        Assert.DoesNotContain(draftName, (await ReadStandards(manager)).GetRawText(), StringComparison.Ordinal);
    }

    private string Code(string name) => name + "-" + _suffix;

    private IJobArchitecture Jobs() => _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();

    private ITaxonomyAuthoring Taxonomy() => _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();

    private IBenchmarkAuthoring Benchmarks() => _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();

    private IReferenceLibrary Reference() => _infrastructure.Api.Services.GetRequiredService<IReferenceLibrary>();

    private async Task ClearDrafts()
    {
        await Execute("DELETE FROM benchmark.target WHERE benchmark_version_id IN (SELECT benchmark_version_id FROM benchmark.version WHERE status = 'draft')");
        await Execute("DELETE FROM benchmark.version WHERE status = 'draft'");
        await Execute("DELETE FROM taxonomy.version_skill WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')");
        await Execute("DELETE FROM taxonomy.proficiency WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')");
        await Execute("DELETE FROM taxonomy.version WHERE status = 'draft'");
    }

    private async Task InsertDraftBenchmark(Guid versionId, Guid profileId, Guid taxonomyVersionId)
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO benchmark.version (
                benchmark_version_id, job_profile_id, taxonomy_version_id, version_number, status, created_at)
            VALUES ($1, $2, $3, 1, 'draft', now())
            """,
            connection);
        command.Parameters.AddWithValue(versionId);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(taxonomyVersionId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "standards-" + Guid.NewGuid().ToString("N"));
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
        var shell = await ReadShell(client);
        var response = await Post(client, "/api/signin", new { subjectId }, shell.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return await ReadShell(client);
    }

    private static async Task<JsonElement> ReadShell(HttpClient client)
    {
        var response = await client.GetAsync("/api/shell");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static async Task<JsonElement> ReadStandards(HttpClient client)
    {
        var response = await client.GetAsync("/api/standards");
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

    private async Task Execute(string sql, Guid? id = null)
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is Guid value)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> Count(string sql, Guid id)
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountText(string sql, string text, Guid? id = null)
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(text);
        if (id is Guid value)
            command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string> ScalarString(string sql, Guid id)
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> ScalarGuid(string sql, Guid id)
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<NpgsqlConnection> Open()
    {
        var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        return connection;
    }
}
