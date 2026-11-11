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
public sealed class ActivationProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private Guid _waveId;

    public ActivationProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task The_activation_centre_launches_a_wave_and_admits_one_mapped_joiner()
    {
        var launched = "person-single";
        var joiner = "act-join-" + _suffix;
        var early = "act-early-" + _suffix;
        var unmapped = "act-other-" + _suffix;
        var missing = "act-missing-" + _suffix;
        try
        {
            await ClearDrafts();
            using var api = _infrastructure.Api.CreateClient();
            var budget = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 1 });
            Assert.Equal(HttpStatusCode.NoContent, budget.StatusCode);

            var published = await PublishBenchmark();
            var over = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
            {
                benchmarkVersionId = published,
                subjectIds = new[] { launched, "person-multi" }
            }, "over-" + _suffix);
            Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
            Assert.Equal("That population is over the zone budget.", await Detail(over));
            Assert.Equal(0, await Count("SELECT count(*) FROM activation.wave WHERE benchmark_version_id = $1", published));

            var admitted = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
            {
                benchmarkVersionId = published,
                subjectIds = new[] { launched }
            }, "launch-" + _suffix);
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
            _waveId = JsonSerializer.Deserialize<JsonElement>(await admitted.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();
            Assert.Equal("launch", await Text("SELECT route FROM activation.admission WHERE wave_id = $1 AND subject_id = $2", _waveId, launched));

            var employee = await Send(api, HttpMethod.Get, "/activation/centre", "person-single", "employee", null);
            Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);

            var centre = await Centre(api);
            Assert.Equal(1, centre.GetProperty("sittingBudget").GetInt32());
            var listed = Wave(centre);
            Assert.Equal(1, listed.GetProperty("admittedCount").GetInt32());
            Assert.Equal(launched, listed.GetProperty("subjectIds")[0].GetString());

            await Insert(joiner, "LE-LOCAL", "plus");
            var overRoll = await Send(api, HttpMethod.Post, "/commands/waves/rolling", "person-ld", "learning_and_development", new { waveId = _waveId, subjectId = joiner }, "roll-over-" + _suffix);
            Assert.Equal(HttpStatusCode.BadRequest, overRoll.StatusCode);
            Assert.Equal("That population is over the zone budget.", await Detail(overRoll));
            Assert.Equal(0, await CountText("SELECT count(*) FROM activation.admission WHERE subject_id = $1", joiner));

            var raised = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 2 });
            Assert.Equal(HttpStatusCode.NoContent, raised.StatusCode);

            await Insert(early, "LE-LOCAL", "minus");
            var earlyRoll = await Send(api, HttpMethod.Post, "/commands/waves/rolling", "person-ld", "learning_and_development", new { waveId = _waveId, subjectId = early }, "roll-early-" + _suffix);
            Assert.Equal(HttpStatusCode.BadRequest, earlyRoll.StatusCode);
            Assert.Equal("That person joined before this wave opened.", await Detail(earlyRoll));
            Assert.Equal(0, await CountText("SELECT count(*) FROM activation.admission WHERE subject_id = $1", early));

            await Insert(unmapped, "LE-OTHER", "plus");
            var unmappedRoll = await Send(api, HttpMethod.Post, "/commands/waves/rolling", "person-ld", "learning_and_development", new { waveId = _waveId, subjectId = unmapped }, "roll-unmap-" + _suffix);
            Assert.Equal(HttpStatusCode.BadRequest, unmappedRoll.StatusCode);
            Assert.Equal("That legal entity is not mapped to this zone.", await Detail(unmappedRoll));
            Assert.Equal(0, await CountText("SELECT count(*) FROM activation.admission WHERE subject_id = $1", unmapped));

            var absent = await Send(api, HttpMethod.Post, "/commands/waves/rolling", "person-ld", "learning_and_development", new { waveId = _waveId, subjectId = missing }, "roll-miss-" + _suffix);
            Assert.Equal(HttpStatusCode.BadRequest, absent.StatusCode);
            Assert.Equal("That person is not in this zone.", await Detail(absent));
            Assert.Equal(0, await CountText("SELECT count(*) FROM activation.admission WHERE subject_id = $1", missing));
            Assert.Equal(0, await CountText("SELECT count(*) FROM workforce.person WHERE employee_id = $1", missing));

            var rolled = await Send(api, HttpMethod.Post, "/commands/waves/rolling", "person-ld", "learning_and_development", new { waveId = _waveId, subjectId = joiner }, "roll-" + _suffix);
            var rolledBody = JsonSerializer.Deserialize<JsonElement>(await rolled.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, rolled.StatusCode);
            Assert.Equal(_waveId, rolledBody.GetProperty("waveId").GetGuid());
            Assert.Equal(2, rolledBody.GetProperty("population").GetInt32());
            Assert.Equal("rolling", await Text("SELECT route FROM activation.admission WHERE wave_id = $1 AND subject_id = $2", _waveId, joiner));
            Assert.Equal("launch", await Text("SELECT route FROM activation.admission WHERE wave_id = $1 AND subject_id = $2", _waveId, launched));
            Assert.Equal(2, await Count("SELECT population FROM activation.wave WHERE wave_id = $1", _waveId));

            var again = await Send(api, HttpMethod.Post, "/commands/waves/rolling", "person-ld", "learning_and_development", new { waveId = _waveId, subjectId = joiner }, "roll-again-" + _suffix);
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
            Assert.Equal("That person is already admitted to this wave.", await Detail(again));
            Assert.Equal(2, await Count("SELECT count(*) FROM activation.admission WHERE wave_id = $1", _waveId));

            var afterCentre = await Centre(api);
            var after = Wave(afterCentre);
            var people = after.GetProperty("subjectIds").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Equal(2, afterCentre.GetProperty("sittingBudget").GetInt32());
            Assert.Equal(2, after.GetProperty("admittedCount").GetInt32());
            Assert.Contains(launched, people);
            Assert.Contains(joiner, people);

            using var web = WebClient();
            await SignIn(web, "person-ld");
            var entry = await Read(web, "/api/shell");
            if (entry.TryGetProperty("requestToken", out var token) && token.ValueKind == JsonValueKind.String)
            {
                var chosen = await Post(web, "/api/workspace", new { workspace = "learning_and_development" }, token.GetString()!);
                Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
            }
            var shell = await Read(web, "/api/activation/centre");
            Assert.Equal(2, Wave(shell).GetProperty("admittedCount").GetInt32());
        }
        finally
        {
            if (_waveId != Guid.Empty)
            {
                await Execute("DELETE FROM activation.admission WHERE wave_id = $1", _waveId);
                await Execute("DELETE FROM activation.wave WHERE wave_id = $1", _waveId);
            }

            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", joiner);
            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", early);
            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", unmapped);
            await Execute("DELETE FROM messaging.reference_delivery WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM reference.applied_message WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM messaging.outbox WHERE correlation_id LIKE $1", "%" + _suffix + "%");
        }
    }

    private async Task<Guid> PublishBenchmark()
    {
        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        await jobs.ImportAsync(
            [
                new FrameworkImport(
                    Code("field"),
                    "Field",
                    [new LevelImport(Code("apprentice"), "Apprentice", 1)],
                    [new ProfileImport(Code("analyst"), "Activation analyst", Code("apprentice"))])
            ],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [new PracticeDraft(Code("practice"), "Activation practice", [new CompetencyDraft(Code("competency"), "Activation competency", [new SkillDraft(Code("skill"), "Activation measure")])])],
                [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]),
            CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "taxonomy-" + _suffix, CancellationToken.None);
        var draftBenchmark = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(publishedTaxonomy.Skills.Single().SkillId, 2)]),
            CancellationToken.None);
        var published = await benchmarks.PublishAsync(draftBenchmark.BenchmarkVersionId, "person-ld", "benchmark-" + _suffix, CancellationToken.None);
        return published.BenchmarkVersionId;
    }

    private JsonElement Wave(JsonElement centre) =>
        centre.GetProperty("waves").EnumerateArray().Single(wave => wave.GetProperty("waveId").GetGuid() == _waveId);

    private async Task<JsonElement> Centre(HttpClient api)
    {
        var response = await Send(api, HttpMethod.Get, "/activation/centre", "person-ld", "learning_and_development", null, "centre-" + _suffix);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task Insert(string employeeId, string legalEntityId, string when)
    {
        var shift = when == "plus" ? "created_at + interval '1 minute'" : "created_at - interval '1 day'";
        await Execute(
            $"""
            INSERT INTO workforce.person (
                employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                personal_learning_assigned, display_name, projected_at, joined_at)
            SELECT $1, $2, 'IN', 'person-multi', 'active', true, $1, now(), {shift}
            FROM activation.wave
            WHERE wave_id = $3
            """,
            employeeId,
            legalEntityId,
            _waveId);
    }

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body, string? correlationId = null)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", correlationId ?? "activation-" + _suffix);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<string> Detail(HttpResponseMessage response)
    {
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return json.GetProperty("detail").GetString()!;
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
        foreach (var sql in new[]
        {
            "DELETE FROM benchmark.target WHERE benchmark_version_id IN (SELECT benchmark_version_id FROM benchmark.version WHERE status = 'draft')",
            "DELETE FROM benchmark.version WHERE status = 'draft'",
            "DELETE FROM taxonomy.version_skill WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')",
            "DELETE FROM taxonomy.proficiency WHERE taxonomy_version_id IN (SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft')",
            "DELETE FROM taxonomy.version WHERE status = 'draft'"
        })
            await Execute(sql);
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

    private async Task<int> Count(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
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

    private async Task<string> Text(string sql, Guid id, string subjectId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(subjectId);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
