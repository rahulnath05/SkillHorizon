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
public sealed class AssessmentOperationsProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private Guid _benchmarkId;
    private Guid _waveId;

    public AssessmentOperationsProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Studio_snapshots_keep_declarations_apart_from_sealed_results()
    {
        var prompt = "ops-prompt-" + _suffix;
        var skill = Code("skill");
        var joiner = "ops-joiner-" + _suffix;
        var overstated = "Overstated-" + _suffix;
        try
        {
            await ClearDrafts();
            _benchmarkId = await PublishBenchmark();
            await InsertItem(skill, prompt);
            using var api = _infrastructure.Api.CreateClient();
            using var learning = _infrastructure.Learning.CreateClient();

            foreach (var (method, path, body) in EmployeeRoutes())
            {
                var client = path.StartsWith("/studio/declarations", StringComparison.Ordinal) || path is "/studio/declared" or "/studio/rolling" or "/studio/overview"
                    ? api
                    : learning;
                var refused = await Send(client, method, path, "person-single", "employee", body);
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            }

            Assert.Equal(0, await Count("SELECT count(*) FROM readiness.declaration WHERE subject_id = $1 AND skill_code = $2", "person-single", skill));
            var budget = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 2 });
            Assert.Equal(HttpStatusCode.NoContent, budget.StatusCode);
            var admitted = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
            {
                benchmarkVersionId = _benchmarkId,
                subjectIds = new[] { "person-single", "person-multi" }
            });
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
            _waveId = JsonSerializer.Deserialize<JsonElement>(await admitted.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();

            var before = await Snapshot(learning, "operations");
            var single = await Open(learning, api, "person-single");
            Assert.Equal(prompt, single.Item.GetProperty("prompt").GetString());
            await Open(learning, api, "person-multi");
            await Task.Delay(1100);
            var answered = await Answer(learning, single, 1);
            Assert.True(answered.Ready);
            var sealedResult = await LearningSend(learning, HttpMethod.Post, $"/sittings/{single.SittingId}/seal", single.Token, null);
            Assert.Equal(HttpStatusCode.OK, sealedResult.StatusCode);
            Assert.DoesNotContain(prompt, await sealedResult.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var afterBody = await Snapshot(learning, "operations");
            var scorecard = await Snapshot(learning, "scorecard");
            Assert.Equal(before.Completed + 1, afterBody.Completed);
            Assert.Equal(before.Open + 1, afterBody.Open);
            Assert.True(afterBody.Duration >= before.Duration);
            Assert.Equal(afterBody.Completed, scorecard.Completed);
            Assert.Equal(afterBody.Open, scorecard.Open);
            Assert.DoesNotContain(prompt, afterBody.Raw, StringComparison.Ordinal);
            Assert.DoesNotContain(prompt, scorecard.Raw, StringComparison.Ordinal);

            var pilotWrite = await Send(learning, HttpMethod.Post, "/studio/pilot", "person-ld", "learning_and_development", new { waveId = _waveId });
            Assert.Equal(HttpStatusCode.OK, pilotWrite.StatusCode);
            var pilot = await Snapshot(learning, "pilot?wave=" + _waveId);
            Assert.Equal(1, pilot.Completed);
            Assert.Equal(1, pilot.Open);
            Assert.True(pilot.Duration >= 1);
            Assert.False(string.IsNullOrWhiteSpace(pilot.AsOf));
            Assert.DoesNotContain(prompt, pilot.Raw, StringComparison.Ordinal);
            Assert.Equal(0, await AssessmentCount("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'assessment' AND table_name = 'programme_snapshot' AND column_name = $1", "response_body"));

            Assert.Equal(1, await Count("SELECT proficiency_rank FROM readiness.proficiency AS proficiency JOIN readiness.sealed_result AS result ON result.result_id = proficiency.result_id WHERE result.subject_id = 'person-single' AND proficiency.skill_code = $1 ORDER BY result.sealed_at DESC LIMIT 1", skill));
            await Execute(
                """
                INSERT INTO workforce.person (
                    employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                    personal_learning_assigned, display_name, projected_at)
                VALUES ('person-single', 'LE-LOCAL', 'IN', 'person-multi', 'active', true, 'person-single', now())
                ON CONFLICT (employee_id) DO NOTHING
                """);
            var declared = await Send(api, HttpMethod.Post, "/studio/declarations", "person-ld", "learning_and_development", new
            {
                subjectId = "person-single",
                skillCode = skill,
                declaredRank = 4,
                declaredName = overstated
            });
            var declaredBody = await declared.Content.ReadAsStringAsync();
            Assert.True(declared.StatusCode == HttpStatusCode.OK, declaredBody);
            Assert.Equal(4, JsonSerializer.Deserialize<JsonElement>(declaredBody).GetProperty("declaredRank").GetInt32());
            Assert.Equal(1, JsonSerializer.Deserialize<JsonElement>(declaredBody).GetProperty("assessedRank").GetInt32());
            Assert.Equal(1, await Count("SELECT proficiency_rank FROM readiness.proficiency AS proficiency JOIN readiness.sealed_result AS result ON result.result_id = proficiency.result_id WHERE result.subject_id = 'person-single' AND proficiency.skill_code = $1 ORDER BY result.sealed_at DESC LIMIT 1", skill));
            Assert.DoesNotContain(prompt, declaredBody, StringComparison.Ordinal);

            await InsertJoiner(joiner);
            var raised = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 3 });
            Assert.Equal(HttpStatusCode.NoContent, raised.StatusCode);
            var rolled = await Send(api, HttpMethod.Post, "/commands/waves/rolling", "person-ld", "learning_and_development", new { waveId = _waveId, subjectId = joiner });
            Assert.Equal(HttpStatusCode.OK, rolled.StatusCode);
            var rolling = await Send(api, HttpMethod.Get, "/studio/rolling", "person-ld", "learning_and_development", null);
            var rollingBody = await rolling.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, rolling.StatusCode);
            Assert.Contains(joiner, rollingBody, StringComparison.Ordinal);
            Assert.DoesNotContain("person-single", JsonSerializer.Deserialize<JsonElement>(rollingBody).GetProperty("rolling").GetRawText(), StringComparison.Ordinal);
            var overview = await Send(api, HttpMethod.Get, "/studio/overview", "person-ld", "learning_and_development", null);
            var overviewBody = await overview.Content.ReadAsStringAsync();
            Assert.Contains(joiner, overviewBody, StringComparison.Ordinal);
            Assert.True(JsonSerializer.Deserialize<JsonElement>(overviewBody).GetProperty("launchCount").GetInt32() >= 2);
            Assert.DoesNotContain(prompt, rollingBody, StringComparison.Ordinal);

            Assert.Equal(0, await Scan(_infrastructure.DomainConnection, prompt));
            Assert.Equal(0, await Scan(_infrastructure.ProgressConnection, prompt));
            var logs = string.Join('\n', _infrastructure.Logs.Messages);
            Assert.DoesNotContain(prompt, logs, StringComparison.Ordinal);

            using var web = WebClient();
            await SignIn(web, "person-ld");
            var entry = await Read(web, "/api/shell");
            var chosen = await Post(web, "/api/workspace", new { workspace = "learning_and_development" }, entry.GetProperty("requestToken").GetString()!);
            Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
            var shellOps = await Read(web, "/api/studio/operations");
            Assert.Equal(afterBody.Completed, shellOps.GetProperty("snapshot").GetProperty("completedCount").GetInt32());
            Assert.DoesNotContain(prompt, shellOps.GetRawText(), StringComparison.Ordinal);

            using var employeeWeb = WebClient();
            await SignIn(employeeWeb, "person-single");
            foreach (var path in new[] { "/api/studio/bank", "/api/studio/operations", "/api/studio/scorecard", "/api/studio/pilot?wave=" + _waveId, "/api/studio/declared", "/api/studio/rolling", "/api/studio/overview" })
            {
                var response = await employeeWeb.GetAsync(path);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.DoesNotContain(prompt, body, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (_benchmarkId != Guid.Empty)
            {
                await Assessment("UPDATE assessment.sitting SET status = 'open' WHERE benchmark_version_id = $1", _benchmarkId);
                await Assessment("DELETE FROM assessment.mark WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE benchmark_version_id = $1)", _benchmarkId);
                await Assessment("DELETE FROM assessment.sealed_proficiency WHERE result_id IN (SELECT result_id FROM assessment.sealed_result WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE benchmark_version_id = $1))", _benchmarkId);
                await Assessment("DELETE FROM assessment.sealed_result WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE benchmark_version_id = $1)", _benchmarkId);
                await Assessment("DELETE FROM assessment.response WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE benchmark_version_id = $1)", _benchmarkId);
                await Assessment("DELETE FROM assessment.sitting WHERE benchmark_version_id = $1", _benchmarkId);
                await Assessment("DELETE FROM assessment.choice WHERE item_id IN (SELECT item_id FROM assessment.item WHERE skill_code = $1)", skill);
                await Assessment("DELETE FROM assessment.item WHERE skill_code = $1", skill);
            }

            if (_waveId != Guid.Empty)
                await Assessment("DELETE FROM assessment.programme_snapshot WHERE wave_id = $1", _waveId);
            await Execute("DELETE FROM readiness.declaration WHERE subject_id = $1 AND skill_code = $2", "person-single", skill);
            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", joiner);
            if (_waveId != Guid.Empty)
            {
                await Execute("DELETE FROM activation.sitting_authorization WHERE wave_id = $1", _waveId);
                await Execute("DELETE FROM activation.admission WHERE wave_id = $1", _waveId);
                await Execute("DELETE FROM activation.wave WHERE wave_id = $1", _waveId);
            }

            await Execute("DELETE FROM messaging.reference_delivery WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM reference.applied_message WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM messaging.outbox WHERE correlation_id LIKE $1", "%" + _suffix + "%");
        }
    }

    private IEnumerable<(HttpMethod Method, string Path, object? Body)> EmployeeRoutes()
    {
        yield return (HttpMethod.Get, "/studio/bank", null);
        yield return (HttpMethod.Post, "/studio/items", new { skillCode = "x", prompt = "x", difficulty = 1 });
        yield return (HttpMethod.Get, "/studio/marks", null);
        yield return (HttpMethod.Get, "/studio/panel", null);
        yield return (HttpMethod.Post, "/studio/panel", new { domainName = "x", expertName = "x", credentials = "x" });
        yield return (HttpMethod.Post, "/studio/snapshots", new { });
        yield return (HttpMethod.Get, "/studio/operations", null);
        yield return (HttpMethod.Get, "/studio/scorecard", null);
        yield return (HttpMethod.Post, "/studio/pilot", new { waveId = Guid.NewGuid() });
        yield return (HttpMethod.Get, "/studio/pilot?wave=" + Guid.NewGuid(), null);
        yield return (HttpMethod.Post, "/studio/declarations", new { subjectId = "person-single", skillCode = "x", declaredRank = 1, declaredName = "x" });
        yield return (HttpMethod.Get, "/studio/declared", null);
        yield return (HttpMethod.Get, "/studio/rolling", null);
        yield return (HttpMethod.Get, "/studio/overview", null);
    }

    private async Task<Counted> Snapshot(HttpClient learning, string path)
    {
        if (path is "operations" or "scorecard")
        {
            var taken = await Send(learning, HttpMethod.Post, "/studio/snapshots", "person-ld", "learning_and_development", new { });
            Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        }

        var response = await Send(learning, HttpMethod.Get, "/studio/" + path, "person-ld", "learning_and_development", null);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        var snapshot = JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("snapshot");
        return new Counted(
            snapshot.GetProperty("completedCount").GetInt32(),
            snapshot.GetProperty("openCount").GetInt32(),
            snapshot.GetProperty("durationSeconds").GetInt32(),
            snapshot.GetProperty("asOf").GetString() ?? "",
            raw);
    }

    private async Task<Guid> PublishBenchmark()
    {
        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        await jobs.ImportAsync(
            [new FrameworkImport(Code("field"), "Field", [new LevelImport(Code("apprentice"), "Apprentice", 1)], [new ProfileImport(Code("analyst"), "Operations analyst", Code("apprentice"))])],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [new PracticeDraft(Code("practice"), "Operations practice", [new CompetencyDraft(Code("competency"), "Operations competency", [new SkillDraft(Code("skill"), "Operations measure")])])],
                [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]),
            CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "taxonomy-" + _suffix, CancellationToken.None);
        var draft = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(publishedTaxonomy.Skills.Single().SkillId, 2)]),
            CancellationToken.None);
        var published = await benchmarks.PublishAsync(draft.BenchmarkVersionId, "person-ld", "benchmark-" + _suffix, CancellationToken.None);
        return published.BenchmarkVersionId;
    }

    private async Task InsertItem(string skillCode, string prompt)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        var itemId = Guid.NewGuid();
        await using (var item = new NpgsqlCommand("INSERT INTO assessment.item (item_id, skill_code, prompt, difficulty) VALUES ($1, $2, $3, 2)", connection))
        {
            item.Parameters.AddWithValue(itemId);
            item.Parameters.AddWithValue(skillCode);
            item.Parameters.AddWithValue(prompt);
            await item.ExecuteNonQueryAsync();
        }

        await using var choice = new NpgsqlCommand("INSERT INTO assessment.choice (item_id, rank, label) VALUES ($1, 1, 'Held'), ($1, 2, 'Reached')", connection);
        choice.Parameters.AddWithValue(itemId);
        await choice.ExecuteNonQueryAsync();
    }

    private async Task InsertJoiner(string employeeId)
    {
        await Execute(
            """
            INSERT INTO workforce.person (
                employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                personal_learning_assigned, display_name, projected_at, joined_at)
            SELECT $1, 'LE-LOCAL', 'IN', 'person-multi', 'active', true, $1, now(), created_at + interval '1 minute'
            FROM activation.wave
            WHERE wave_id = $2
            """,
            employeeId,
            _waveId);
    }

    private async Task<Opened> Open(HttpClient learning, HttpClient api, string subjectId)
    {
        var authorized = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", subjectId, "employee", new { waveId = _waveId });
        var body = await authorized.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var token = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("token").GetString()!;
        var opened = await LearningSend(learning, HttpMethod.Post, "/sittings", token, null);
        var openedBody = await opened.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(openedBody);
        return new Opened(json.GetProperty("sittingId").GetGuid(), token, json.GetProperty("item"), json.GetProperty("readyToSeal").GetBoolean());
    }

    private static async Task<Opened> Answer(HttpClient learning, Opened sitting, int rank)
    {
        var answered = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.SittingId}/responses", sitting.Token, new
        {
            itemId = sitting.Item.GetProperty("itemId").GetGuid(),
            rank
        });
        var body = await answered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        return new Opened(sitting.SittingId, sitting.Token, sitting.Item, json.GetProperty("readyToSeal").GetBoolean());
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "ops-" + _suffix);
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

    private Task Execute(string sql, params object[] values) => ExecuteOn(_infrastructure.DomainConnection, sql, values);

    private Task Assessment(string sql, params object[] values) => ExecuteOn(_infrastructure.AssessmentConnection, sql, values);

    private static async Task ExecuteOn(string connectionString, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> Count(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> AssessmentCount(string sql, string text)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(text);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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

    private sealed record Opened(Guid SittingId, string Token, JsonElement Item, bool Ready);

    private sealed record Counted(int Completed, int Open, int Duration, string AsOf, string Raw);
}
