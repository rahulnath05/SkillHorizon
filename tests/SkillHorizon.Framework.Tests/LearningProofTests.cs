extern alias LearningHost;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Taxonomy;
using Learning = LearningHost::SkillHorizon.Learning;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class LearningProofTests
{
    private const string Prompt = "zeta-item-prompt";
    private const string Choice = "zeta-choice-label";

    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public LearningProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task A_sitting_seals_a_gap_without_copying_item_text_into_the_domain()
    {
        await ClearDrafts();
        using var api = _infrastructure.Api.CreateClient();
        var budget = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 1 });
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
                    [new ProfileImport(Code("analyst"), "Field analyst", Code("apprentice"))])
            ],
            CancellationToken.None);
        var profile = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles.Single();
        var drafted = await taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [new PracticeDraft(Code("practice"), "Field practice", [new CompetencyDraft(Code("competency"), "Field competency", [new SkillDraft(Code("skill"), "Field measure")])])],
                [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]),
            CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "taxonomy-" + _suffix, CancellationToken.None);
        var skill = publishedTaxonomy.Skills.Single();
        var draftBenchmark = await benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(profile.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 2)]),
            CancellationToken.None);
        var overDraft = "over-draft-" + _suffix;
        var refusedDraft = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
        {
            benchmarkVersionId = draftBenchmark.BenchmarkVersionId,
            subjectIds = new[] { "person-single" }
        }, overDraft);
        Assert.Equal(HttpStatusCode.BadRequest, refusedDraft.StatusCode);
        Assert.Equal(0, await Count("SELECT count(*) FROM activation.wave WHERE benchmark_version_id = $1", draftBenchmark.BenchmarkVersionId));
        Assert.Equal(0, await CountText("SELECT count(*) FROM audit.audit_record WHERE correlation_id = $1", overDraft));

        var published = await benchmarks.PublishAsync(draftBenchmark.BenchmarkVersionId, "person-ld", "benchmark-" + _suffix, CancellationToken.None);
        Assert.Equal("Reached", published.Targets.Single().ProficiencyName);
        await InsertItem(skill.Code);

        var over = "over-budget-" + _suffix;
        var refused = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
        {
            benchmarkVersionId = published.BenchmarkVersionId,
            subjectIds = new[] { "person-single", "person-multi" }
        }, over);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, await Count("SELECT count(*) FROM activation.wave WHERE benchmark_version_id = $1", published.BenchmarkVersionId));
        Assert.Equal(0, await CountText("SELECT count(*) FROM audit.audit_record WHERE correlation_id = $1", over));

        var admitted = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
        {
            benchmarkVersionId = published.BenchmarkVersionId,
            subjectIds = new[] { "person-single" }
        });
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        var wave = JsonSerializer.Deserialize<JsonElement>(await admitted.Content.ReadAsStringAsync());
        var waveId = wave.GetProperty("waveId").GetGuid();
        var second = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
        {
            benchmarkVersionId = published.BenchmarkVersionId,
            subjectIds = new[] { "person-other" }
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var otherWave = JsonSerializer.Deserialize<JsonElement>(await second.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();

        var manager = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", "person-multi", "people_manager", new { waveId });
        var author = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", "person-ld", "learning_and_development", new { waveId });
        var outsider = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", "person-single", "employee", new { waveId = otherWave });
        var managerView = await Send(api, HttpMethod.Get, "/assessment", "person-multi", "people_manager", null);
        Assert.Equal(HttpStatusCode.Forbidden, manager.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, author.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outsider.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, managerView.StatusCode);

        var authorized = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", "person-single", "employee", new { waveId });
        var authorizedBody = await authorized.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var grant = JsonSerializer.Deserialize<JsonElement>(authorizedBody);
        var token = grant.GetProperty("token").GetString()!;
        var authorizationId = grant.GetProperty("authorizationId").GetGuid();
        Assert.DoesNotContain(Prompt, authorizedBody, StringComparison.Ordinal);

        using var learning = _infrastructure.Learning.CreateClient();
        var opened = await LearningSend(learning, HttpMethod.Post, "/sittings", token, null);
        var openedBody = await opened.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Contains(Prompt, openedBody, StringComparison.Ordinal);
        Assert.Contains(Choice, openedBody, StringComparison.Ordinal);
        var sitting = JsonSerializer.Deserialize<JsonElement>(openedBody);
        var sittingId = sitting.GetProperty("sittingId").GetGuid();
        var itemId = sitting.GetProperty("item").GetProperty("itemId").GetGuid();

        var answered = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sittingId}/responses", token, new { itemId, rank = 1 });
        var answeredBody = await answered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.True(JsonSerializer.Deserialize<JsonElement>(answeredBody).GetProperty("readyToSeal").GetBoolean());
        Assert.Equal(1, await AssessmentCount("SELECT count(*) FROM assessment.response WHERE sitting_id = $1 AND selected_rank = 1", sittingId));

        var sealedOnce = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sittingId}/seal", token, null);
        var sealedBody = await sealedOnce.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, sealedOnce.StatusCode);
        Assert.Contains("Held", sealedBody, StringComparison.Ordinal);
        Assert.Contains("Reached", sealedBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Prompt, sealedBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, sealedBody, StringComparison.Ordinal);
        var resultId = JsonSerializer.Deserialize<JsonElement>(sealedBody).GetProperty("resultId").GetGuid();

        var sealedAgain = await LearningSend(learning, HttpMethod.Post, $"/sittings/{sittingId}/seal", token, null);
        Assert.Equal(HttpStatusCode.OK, sealedAgain.StatusCode);
        Assert.Equal(resultId, JsonSerializer.Deserialize<JsonElement>(await sealedAgain.Content.ReadAsStringAsync()).GetProperty("resultId").GetGuid());
        Assert.Equal(1, await CountText("SELECT count(*) FROM readiness.sealed_result WHERE subject_id = $1", "person-single"));
        Assert.Equal("Held", await Scalar("SELECT proficiency_name FROM readiness.proficiency WHERE result_id = $1", resultId));
        Assert.Equal(1, await Count("SELECT count(*) FROM audit.audit_record WHERE action = 'result.sealed' AND record_id = $1", resultId));

        var echoed = await LearningKey(api, HttpMethod.Post, "/internal/results", new
        {
            authorizationId,
            resultId,
            proficiencies = new[] { new { skillCode = skill.Code, skillName = skill.Name, rank = 1, code = Code("held"), name = "Held" } },
            prompt = Prompt,
            label = Choice
        });
        Assert.Equal(HttpStatusCode.OK, echoed.StatusCode);
        Assert.Equal(1, await CountText("SELECT count(*) FROM readiness.sealed_result WHERE subject_id = $1", "person-single"));

        Assert.Equal(0, await Scan(_infrastructure.DomainConnection, Prompt));
        Assert.Equal(0, await Scan(_infrastructure.DomainConnection, Choice));
        Assert.Equal(0, await Scan(_infrastructure.ProgressConnection, Prompt));
        Assert.Equal(0, await Scan(_infrastructure.ProgressConnection, Choice));
        Assert.True(await Scan(_infrastructure.AssessmentConnection, Prompt) > 0);
        Assert.True(await Scan(_infrastructure.AssessmentConnection, Choice) > 0);
        var logs = string.Join('\n', _infrastructure.Logs.Messages);
        Assert.DoesNotContain(Prompt, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, logs, StringComparison.Ordinal);

        var immutable = await Assert.ThrowsAsync<PostgresException>(() => Execute(
            "UPDATE readiness.proficiency SET proficiency_name = 'Rewritten' WHERE result_id = $1",
            resultId));
        Assert.Contains("immutable", immutable.MessageText, StringComparison.OrdinalIgnoreCase);

        var catalog = _infrastructure.Learning.Services.GetRequiredService<Learning.IExternalCatalog>();
        Assert.Equal(0, catalog.Calls);
        var bearer = await _infrastructure.IssueTokenAsync("person-single", "LE-LOCAL");
        var external = await LearningSend(learning, HttpMethod.Post, "/progress/steps", null, new { provider = "external", title = "Outside course" }, bearer, "employee");
        Assert.Equal(HttpStatusCode.NoContent, external.StatusCode);
        Assert.Equal(0, catalog.Calls);
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.step WHERE subject_id = 'person-single' AND provider = 'external' AND title = 'Outside course'"));
        Assert.Equal(1, await ProgressCount("SELECT count(*) FROM progress.step WHERE provider = 'skillhorizon_assessment' AND result_id = $1", resultId));

        using var employee = WebClient();
        await SignIn(employee, "person-single");
        var assessment = await employee.GetAsync("/api/assessment");
        var assessmentBody = await assessment.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, assessment.StatusCode);
        Assert.Contains("Held", assessmentBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Prompt, assessmentBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, assessmentBody, StringComparison.Ordinal);

        using var peopleManager = WebClient();
        var entry = await SignIn(peopleManager, "person-multi");
        var chosen = await Post(peopleManager, "/api/workspace", new { workspace = "people_manager" }, entry.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        var hidden = await peopleManager.GetAsync("/api/assessment");
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        var team = await peopleManager.GetAsync("/api/team");
        var teamBody = await team.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, team.StatusCode);
        Assert.DoesNotContain(Prompt, teamBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Choice, teamBody, StringComparison.Ordinal);

        using var unmapped = WebClient();
        var refusedPerson = await SignIn(unmapped, "person-unmapped");
        Assert.Equal("refused", refusedPerson.GetProperty("status").GetString());
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

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body, string? correlationId = null)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", correlationId ?? "learning-" + Guid.NewGuid().ToString("N"));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> LearningKey(HttpClient client, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Learning-Key", FrameworkInfrastructure.LearningKey);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LearningSend(
        HttpClient client,
        HttpMethod method,
        string path,
        string? sittingToken,
        object? body,
        string? bearer = null,
        string? workspace = null)
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

    private async Task Execute(string sql, Guid? id = null)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is Guid value)
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

    private async Task<string> Scalar(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> AssessmentCount(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> ProgressCount(string sql, Guid? id = null)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.ProgressConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is Guid value)
            command.Parameters.AddWithValue(value);
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
}
