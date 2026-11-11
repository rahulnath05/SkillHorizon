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
public sealed class AssessmentBankProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private Guid _benchmarkId;
    private readonly List<Guid> _waves = [];

    public AssessmentBankProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task The_item_bank_marks_a_typed_answer_and_the_next_item_follows_the_previous_one()
    {
        var easy = "bank-easy-" + _suffix;
        var mid = "bank-mid-" + _suffix;
        var hard = "bank-hard-" + _suffix;
        var typed = "bank-typed-" + _suffix;
        var response = "bank-owner-answer-" + _suffix;
        var domain = "Data-" + _suffix;
        var skill = Code("skill");
        var typedSkill = Code("typed");
        try
        {
            await ClearDrafts();
            var published = await PublishBenchmark();
            _benchmarkId = published.BenchmarkId;
            using var learning = _infrastructure.Learning.CreateClient();
            var employeeWrite = await Studio(learning, HttpMethod.Post, "/studio/items", "person-single", "employee", new
            {
                skillCode = skill,
                prompt = easy,
                difficulty = 1,
                choices = new[] { new { rank = 1, label = "Low" }, new { rank = 5, label = "High" } }
            });
            Assert.Equal(HttpStatusCode.Forbidden, employeeWrite.StatusCode);
            Assert.Equal(0, await AssessmentCount("SELECT count(*) FROM assessment.item WHERE prompt = $1", easy));

            foreach (var (prompt, difficulty) in new[] { (easy, 1), (mid, 3), (hard, 5) })
            {
                var written = await Studio(learning, HttpMethod.Post, "/studio/items", "person-ld", "learning_and_development", new
                {
                    skillCode = skill,
                    prompt,
                    difficulty,
                    choices = new[] { new { rank = 1, label = "Low" }, new { rank = 5, label = "High" } }
                });
                Assert.Equal(HttpStatusCode.OK, written.StatusCode);
            }

            var rubric = await Studio(learning, HttpMethod.Post, "/studio/items", "person-ld", "learning_and_development", new
            {
                skillCode = typedSkill,
                prompt = typed,
                difficulty = 2,
                rubric = "1|copy|Repeats the question\n2|owner|Names who must be told"
            });
            Assert.Equal(HttpStatusCode.OK, rubric.StatusCode);

            var bank = await Studio(learning, HttpMethod.Get, "/studio/bank", "person-ld", "learning_and_development", null);
            var bankBody = await bank.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, bank.StatusCode);
            Assert.Contains(skill, bankBody, StringComparison.Ordinal);
            Assert.Contains("\"difficulty\":3", bankBody, StringComparison.Ordinal);
            Assert.Contains(typed, bankBody, StringComparison.Ordinal);

            using var api = _infrastructure.Api.CreateClient();
            var budget = await Send(api, HttpMethod.Post, "/commands/activation/budget", new { sittingBudget = 1 });
            Assert.Equal(HttpStatusCode.NoContent, budget.StatusCode);
            var highWave = await Admit(api, published.BenchmarkId, "person-single");
            var lowWave = await Admit(api, published.BenchmarkId, "person-multi");

            var high = await Open(learning, api, highWave, "person-single");
            Assert.Equal(mid, high.Item.GetProperty("prompt").GetString());
            Assert.False(high.Item.GetProperty("freeText").GetBoolean());
            var harder = await Answer(learning, high, 5, null);
            Assert.Equal(hard, harder.Item.GetProperty("prompt").GetString());

            var low = await Open(learning, api, lowWave, "person-multi");
            Assert.Equal(mid, low.Item.GetProperty("prompt").GetString());
            var freer = await Answer(learning, low, 1, null);
            Assert.Equal(typed, freer.Item.GetProperty("prompt").GetString());
            Assert.True(freer.Item.GetProperty("freeText").GetBoolean());
            Assert.Empty(freer.Item.GetProperty("choices").EnumerateArray());
            var marked = await Answer(learning, freer, 0, response);
            Assert.Equal(2, await AssessmentCount("SELECT band FROM assessment.mark WHERE response_body = $1", response));
            Assert.True(marked.Ready || marked.Item.ValueKind == JsonValueKind.Object);

            var sealedResult = await LearningSend(learning, HttpMethod.Post, $"/sittings/{low.SittingId}/seal", low.Token, null);
            var sealedBody = await sealedResult.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, sealedResult.StatusCode);
            var resultId = JsonSerializer.Deserialize<JsonElement>(sealedBody).GetProperty("resultId").GetGuid();
            Assert.DoesNotContain(typed, sealedBody, StringComparison.Ordinal);
            Assert.DoesNotContain(response, sealedBody, StringComparison.Ordinal);
            Assert.Equal(2, await DomainCount("SELECT proficiency_rank FROM readiness.proficiency WHERE result_id = $1 AND skill_code = $2", resultId, typedSkill));
            Assert.Equal(1, await DomainCount("SELECT proficiency_rank FROM readiness.proficiency WHERE result_id = $1 AND skill_code = $2", resultId, skill));

            Assert.Equal(0, await Scan(_infrastructure.DomainConnection, typed));
            Assert.Equal(0, await Scan(_infrastructure.DomainConnection, response));
            Assert.Equal(0, await Scan(_infrastructure.DomainConnection, mid));
            Assert.Equal(0, await Scan(_infrastructure.ProgressConnection, response));
            Assert.True(await Scan(_infrastructure.AssessmentConnection, response) > 0);
            var logs = string.Join('\n', _infrastructure.Logs.Messages);
            Assert.DoesNotContain(typed, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(response, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(mid, logs, StringComparison.Ordinal);

            var named = await Studio(learning, HttpMethod.Post, "/studio/panel", "person-ld", "learning_and_development", new
            {
                domainName = domain,
                expertName = "Ananya Rao",
                credentials = "15 years on the data platform"
            });
            Assert.Equal(HttpStatusCode.OK, named.StatusCode);
            var panel = await Studio(learning, HttpMethod.Get, "/studio/panel", "person-ld", "learning_and_development", null);
            var panelBody = await panel.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
            Assert.Contains("Ananya Rao", panelBody, StringComparison.Ordinal);
            Assert.DoesNotContain(response, panelBody, StringComparison.Ordinal);
            Assert.Equal(0, await AssessmentCount("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'assessment' AND table_name = 'sme' AND column_name = $1", "response_body"));
            Assert.Equal(0, await AssessmentText("SELECT count(*) FROM assessment.sme WHERE credentials LIKE $1", "%" + response + "%"));

            var employeeBank = await Studio(learning, HttpMethod.Get, "/studio/bank", "person-single", "employee", null);
            Assert.Equal(HttpStatusCode.Forbidden, employeeBank.StatusCode);

            using var web = WebClient();
            await SignIn(web, "person-ld");
            var entry = await Read(web, "/api/shell");
            var chosen = await Post(web, "/api/workspace", new { workspace = "learning_and_development" }, entry.GetProperty("requestToken").GetString()!);
            Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
            var shellBank = await Read(web, "/api/studio/bank");
            Assert.Contains(mid, shellBank.GetRawText(), StringComparison.Ordinal);

            using var employeeWeb = WebClient();
            await SignIn(employeeWeb, "person-single");
            var refused = await employeeWeb.GetAsync("/api/studio/bank");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.DoesNotContain(mid, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            if (_benchmarkId != Guid.Empty)
            {
                await Assessment($"DELETE FROM assessment.mark WHERE item_id IN (SELECT item_id FROM assessment.item WHERE skill_code IN ($1, $2))", skill, typedSkill);
                await Assessment("UPDATE assessment.sitting SET status = 'open' WHERE benchmark_version_id = $1", _benchmarkId);
                await Assessment($"DELETE FROM assessment.sealed_proficiency WHERE result_id IN (SELECT result_id FROM assessment.sealed_result WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE benchmark_version_id = $1))", _benchmarkId);
                await Assessment("DELETE FROM assessment.sealed_result WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE benchmark_version_id = $1)", _benchmarkId);
                await Assessment("DELETE FROM assessment.response WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE benchmark_version_id = $1)", _benchmarkId);
                await Assessment("DELETE FROM assessment.sitting WHERE benchmark_version_id = $1", _benchmarkId);
                await Assessment("DELETE FROM assessment.choice WHERE item_id IN (SELECT item_id FROM assessment.item WHERE skill_code IN ($1, $2))", skill, typedSkill);
                await Assessment("DELETE FROM assessment.rubric WHERE item_id IN (SELECT item_id FROM assessment.item WHERE skill_code IN ($1, $2))", skill, typedSkill);
                await Assessment("DELETE FROM assessment.item WHERE skill_code IN ($1, $2)", skill, typedSkill);
                await Execute("DELETE FROM activation.sitting_authorization WHERE wave_id IN (SELECT wave_id FROM activation.wave WHERE benchmark_version_id = $1)", _benchmarkId);
                await Execute("DELETE FROM activation.admission WHERE wave_id IN (SELECT wave_id FROM activation.wave WHERE benchmark_version_id = $1)", _benchmarkId);
                await Execute("DELETE FROM activation.wave WHERE benchmark_version_id = $1", _benchmarkId);
            }

            await Assessment("DELETE FROM assessment.sme WHERE domain_name = $1", domain);
            await Execute("DELETE FROM messaging.reference_delivery WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM reference.applied_message WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM messaging.outbox WHERE correlation_id LIKE $1", "%" + _suffix + "%");
        }
    }

    private async Task<(Guid BenchmarkId, string Skill, string Typed)> PublishBenchmark()
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
                    [new ProfileImport(Code("analyst"), "Bank analyst", Code("apprentice"))])
            ],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [new PracticeDraft(Code("practice"), "Bank practice", [new CompetencyDraft(Code("competency"), "Bank competency", [new SkillDraft(Code("skill"), "Adaptive measure"), new SkillDraft(Code("typed"), "Typed measure")])])],
                [
                    new ProficiencyDraft(Code("p1"), "Awareness", 1),
                    new ProficiencyDraft(Code("p2"), "Working", 2),
                    new ProficiencyDraft(Code("p3"), "Practitioner", 3),
                    new ProficiencyDraft(Code("p4"), "Advanced", 4),
                    new ProficiencyDraft(Code("p5"), "Authority", 5)
                ]),
            CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "taxonomy-" + _suffix, CancellationToken.None);
        var draft = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId, publishedTaxonomy.Skills.Select(skill => new TargetDraft(skill.SkillId, 5)).ToArray()),
            CancellationToken.None);
        var published = await benchmarks.PublishAsync(draft.BenchmarkVersionId, "person-ld", "benchmark-" + _suffix, CancellationToken.None);
        return (published.BenchmarkVersionId, Code("skill"), Code("typed"));
    }

    private async Task<Guid> Admit(HttpClient api, Guid benchmarkId, string subjectId)
    {
        var admitted = await Send(api, HttpMethod.Post, "/commands/waves/admit", new
        {
            benchmarkVersionId = benchmarkId,
            subjectIds = new[] { subjectId }
        });
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        var waveId = JsonSerializer.Deserialize<JsonElement>(await admitted.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();
        _waves.Add(waveId);
        return waveId;
    }

    private async Task<Opened> Open(HttpClient learning, HttpClient api, Guid waveId, string subjectId)
    {
        var authorized = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", new { waveId }, subjectId, "employee");
        var body = await authorized.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var token = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("token").GetString()!;
        var opened = await LearningSend(learning, HttpMethod.Post, "/sittings", token, null);
        var openedBody = await opened.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(openedBody);
        return new Opened(json.GetProperty("sittingId").GetGuid(), token, json.GetProperty("item"), json.GetProperty("readyToSeal").GetBoolean());
    }

    private async Task<Opened> Answer(HttpClient learning, Opened sitting, int rank, string? responseBody)
    {
        var answered = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sitting.SittingId}/responses", sitting.Token, new
        {
            itemId = sitting.Item.GetProperty("itemId").GetGuid(),
            rank,
            responseBody
        });
        var body = await answered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        var item = json.TryGetProperty("item", out var found) && found.ValueKind == JsonValueKind.Object ? found : default;
        return new Opened(sitting.SittingId, sitting.Token, item, json.GetProperty("readyToSeal").GetBoolean());
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task<HttpResponseMessage> Studio(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "bank-" + _suffix);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body, string subjectId = "person-ld", string workspace = "learning_and_development") =>
        Studio(client, method, path, subjectId, workspace, body);

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

    private async Task Execute(string sql, params object[] values) => await ExecuteOn(_infrastructure.DomainConnection, sql, values);

    private async Task Assessment(string sql, params object[] values) => await ExecuteOn(_infrastructure.AssessmentConnection, sql, values);

    private static async Task ExecuteOn(string connectionString, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> AssessmentCount(string sql, string text)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(text);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> AssessmentText(string sql, string text) => await AssessmentCount(sql, text);

    private async Task<int> DomainCount(string sql, Guid id, string skillCode)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(skillCode);
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
}
