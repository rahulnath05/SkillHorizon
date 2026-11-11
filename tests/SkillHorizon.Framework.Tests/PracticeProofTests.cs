using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Readiness;
using SkillHorizon.Taxonomy;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class PracticeProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public PracticeProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task A_seal_keeps_the_published_baseline_and_the_page_reads_the_snapshot()
    {
        const string member = "practice-person";
        await ClearDrafts();
        await Grant(member, "employee");
        try
        {
            await Prove(member);
        }
        finally
        {
            await Cleanup(member);
        }
    }

    private async Task Prove(string member)
    {
        using var api = _infrastructure.Api.CreateClient();
        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        await jobs.ImportAsync(
            [new FrameworkImport(Code("field"), "Field", [new LevelImport(Code("apprentice"), "Apprentice", 1)],
            [
                new ProfileImport(Code("steady"), "Steady analyst", Code("apprentice")),
                new ProfileImport(Code("shifted"), "Shifted analyst", Code("apprentice"))
            ])],
            CancellationToken.None);
        var profiles = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles;
        var steady = profiles.Single(profile => profile.Code == Code("steady"));
        var shifted = profiles.Single(profile => profile.Code == Code("shifted"));
        var drafted = await taxonomy.SaveDraftAsync(new TaxonomyDraft(
            [new PracticeDraft(Code("practice"), "Field practice", [new CompetencyDraft(Code("competency"), "Field competency", [new SkillDraft(Code("skill"), "Field measure")])])],
            [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]), CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "practice-taxonomy-" + _suffix, CancellationToken.None);
        var skill = publishedTaxonomy.Skills.Single();
        var steadyDraft = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(steady.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 2)]), CancellationToken.None);
        var shiftedDraft = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(shifted.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 2)]), CancellationToken.None);
        var steadyPublished = await benchmarks.PublishAsync(steadyDraft.BenchmarkVersionId, "person-ld", "practice-steady-" + _suffix, CancellationToken.None);
        var shiftedPublished = await benchmarks.PublishAsync(shiftedDraft.BenchmarkVersionId, "person-ld", "practice-shifted-" + _suffix, CancellationToken.None);
        var publishedAt = await DomainTime("SELECT published_at FROM benchmark.version WHERE benchmark_version_id = $1", steadyPublished.BenchmarkVersionId);

        var submitted = await Send(api, HttpMethod.Post, "/commands/packs/submit", "person-ld", "learning_and_development", new
        {
            practiceCode = Code("practice"),
            rows = new object[]
            {
                new { jobProfileId = steady.ProfileId, classification = "aligned" },
                new { jobProfileId = shifted.ProfileId, classification = "tightening", exceptionNote = "market-shift-" + _suffix }
            }
        });
        var submittedBody = await submitted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Contains("aligned", submittedBody, StringComparison.Ordinal);
        Assert.Contains("tightening", submittedBody, StringComparison.Ordinal);
        Assert.Contains("market-shift-" + _suffix, submittedBody, StringComparison.Ordinal);
        var packId = JsonSerializer.Deserialize<JsonElement>(submittedBody).GetProperty("packId").GetGuid();
        Assert.Equal(0, await DomainCount("SELECT count(*) FROM attestation.baseline WHERE practice_code = $1", Code("practice")));
        Assert.Equal(publishedAt, await DomainTime("SELECT published_at FROM benchmark.version WHERE benchmark_version_id = $1", steadyPublished.BenchmarkVersionId));
        Assert.Equal("published", await DomainText("SELECT status FROM benchmark.version WHERE benchmark_version_id = $1", steadyPublished.BenchmarkVersionId));

        var leadSeal = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-lead", "competency_lead", new { });
        var authorSeal = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-ld", "learning_and_development", new { });
        Assert.Equal(HttpStatusCode.Forbidden, leadSeal.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, authorSeal.StatusCode);
        Assert.Equal(0, await DomainCount("SELECT count(*) FROM attestation.baseline WHERE practice_code = $1", Code("practice")));

        var scope = await Send(api, HttpMethod.Post, "/commands/practice-scope", "person-ld", "learning_and_development", new
        {
            subjectId = "person-lead",
            practiceCode = Code("practice")
        });
        Assert.Equal(HttpStatusCode.NoContent, scope.StatusCode);
        var exceptionRowId = JsonSerializer.Deserialize<JsonElement>(submittedBody)
            .GetProperty("rows")
            .EnumerateArray()
            .Single(row => row.GetProperty("classification").GetString() == "tightening")
            .GetProperty("rowId")
            .GetGuid();
        var undecided = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-lead", "competency_lead", new { });
        var undecidedBody = await undecided.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, undecided.StatusCode);
        Assert.Contains("decision", undecidedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await DomainCount("SELECT count(*) FROM attestation.baseline WHERE practice_code = $1", Code("practice")));
        var decided = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/decisions", "person-lead", "competency_lead", new
        {
            rowId = exceptionRowId,
            decision = "Keep the tighter bar"
        });
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        var sealedPack = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-lead", "competency_lead", new { });
        var sealedBody = await sealedPack.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, sealedPack.StatusCode);
        Assert.Contains(steadyPublished.BenchmarkVersionId.ToString(), sealedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(shiftedPublished.BenchmarkVersionId.ToString(), sealedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(packId, await DomainGuid("SELECT pack_id FROM attestation.baseline WHERE practice_code = $1", Code("practice")));
        Assert.Equal(publishedAt, await DomainTime("SELECT published_at FROM benchmark.version WHERE benchmark_version_id = $1", steadyPublished.BenchmarkVersionId));
        Assert.Equal(1, await DomainCount("SELECT count(*) FROM audit.audit_record WHERE action = 'pack.sealed' AND detail::text LIKE $1", "%" + packId + "%"));
        var again = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-lead", "competency_lead", new { });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(1, await DomainCount("SELECT count(*) FROM audit.audit_record WHERE action = 'pack.sealed' AND detail::text LIKE $1", "%" + packId + "%"));

        var limit = await Send(api, HttpMethod.Post, "/commands/cohorts/limit", "person-ld", "learning_and_development", new { maximum = 1 });
        Assert.Equal(HttpStatusCode.NoContent, limit.StatusCode);
        var tooBig = await Send(api, HttpMethod.Post, "/commands/cohorts", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            countryCode = "IN",
            jobProfileId = steady.ProfileId,
            memberIds = new[] { member, "practice-other" }
        });
        var everyone = await Send(api, HttpMethod.Post, "/commands/cohorts", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            orgUnit = "field",
            jobProfileId = steady.ProfileId,
            memberIds = Array.Empty<string>()
        });
        var star = await Send(api, HttpMethod.Post, "/commands/cohorts", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            countryCode = "IN",
            jobProfileId = steady.ProfileId,
            memberIds = new[] { "*" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, everyone.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, star.StatusCode);
        Assert.Equal(0, await DomainCount("SELECT count(*) FROM attestation.cohort WHERE practice_code = $1", Code("practice")));

        await Person(member);
        var cohort = await Send(api, HttpMethod.Post, "/commands/cohorts", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            countryCode = "IN",
            jobProfileId = steady.ProfileId,
            memberIds = new[] { member }
        });
        Assert.Equal(HttpStatusCode.OK, cohort.StatusCode);
        Assert.Equal(1, await DomainCount("SELECT count(*) FROM attestation.cohort_member AS member JOIN attestation.cohort AS cohort ON cohort.cohort_id = member.cohort_id WHERE cohort.practice_code = $1", Code("practice")));

        var admitted = await Send(api, HttpMethod.Post, "/commands/activation/budget", "person-ld", "learning_and_development", new { sittingBudget = 5 });
        Assert.Equal(HttpStatusCode.NoContent, admitted.StatusCode);
        var wave = await Send(api, HttpMethod.Post, "/commands/waves/admit", "person-ld", "learning_and_development", new
        {
            benchmarkVersionId = steadyPublished.BenchmarkVersionId,
            subjectIds = new[] { member }
        });
        var waveId = JsonSerializer.Deserialize<JsonElement>(await wave.Content.ReadAsStringAsync()).GetProperty("waveId").GetGuid();
        var authorized = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", member, "employee", new { waveId });
        var grant = JsonSerializer.Deserialize<JsonElement>(await authorized.Content.ReadAsStringAsync());
        var applied = await LearningKey(api, new
        {
            authorizationId = grant.GetProperty("authorizationId").GetGuid(),
            resultId = Guid.NewGuid(),
            proficiencies = new[] { new { skillCode = skill.Code, skillName = skill.Name, rank = 1, code = Code("held"), name = "Held" } }
        });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        var projection = _infrastructure.Worker.Services.GetRequiredService<IPracticeProjection>();
        Assert.Null(await projection.ReadAsync(Code("practice"), CancellationToken.None));
        await projection.ProjectAsync(CancellationToken.None);
        var snapshot = await projection.ReadAsync(Code("practice"), CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.False(snapshot.Dirty);
        Assert.Equal(1, snapshot.MemberCount);
        Assert.Equal(1, snapshot.MeasuredCount);
        Assert.Equal(1, snapshot.GapCount);
        Assert.Equal(1, snapshot.CohortCount);
        var readiness = await Send(api, HttpMethod.Get, "/practice/readiness?practice=" + Code("practice"), "person-lead", "competency_lead", null);
        var readinessBody = await readiness.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        var shown = JsonSerializer.Deserialize<JsonElement>(readinessBody).GetProperty("snapshot");
        Assert.Equal(snapshot.AsOf.ToUnixTimeMilliseconds(), shown.GetProperty("asOf").GetDateTimeOffset().ToUnixTimeMilliseconds());
        Assert.Equal(1, shown.GetProperty("gapCount").GetInt32());
        Assert.False(shown.GetProperty("dirty").GetBoolean());
        Assert.DoesNotContain(skill.Name, readinessBody, StringComparison.Ordinal);

        var lowered = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(steady.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 1)]), CancellationToken.None);
        await benchmarks.PublishAsync(lowered.BenchmarkVersionId, "person-ld", "practice-lower-" + _suffix, CancellationToken.None);
        var stale = await projection.ReadAsync(Code("practice"), CancellationToken.None);
        Assert.NotNull(stale);
        Assert.True(stale.Dirty);
        Assert.Equal(snapshot.AsOf, stale.AsOf);
        Assert.Equal(snapshot.GapCount, stale.GapCount);
        Assert.Equal(snapshot.MemberCount, stale.MemberCount);

        await projection.ProjectAsync(CancellationToken.None);
        var fresh = await projection.ReadAsync(Code("practice"), CancellationToken.None);
        Assert.NotNull(fresh);
        Assert.False(fresh.Dirty);
        Assert.Equal(0, fresh.GapCount);
        Assert.True(fresh.AsOf > snapshot.AsOf);
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "practice-" + Guid.NewGuid().ToString("N"));
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

    private async Task Person(string employeeId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO workforce.person (
                employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                personal_learning_assigned, display_name, projected_at)
            VALUES ($1, 'LE-LOCAL', 'IN', NULL, 'active', true, $1, now())
            ON CONFLICT (employee_id) DO UPDATE SET employment_status = 'active', closed_at = NULL
            """,
            connection);
        command.Parameters.AddWithValue(employeeId);
        await command.ExecuteNonQueryAsync();
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

    private async Task Cleanup(string employeeId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM workforce.person WHERE employee_id = $1",
            "DELETE FROM access.workspace_grant WHERE subject_id = $1 AND workspace = 'employee'"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(employeeId);
            await command.ExecuteNonQueryAsync();
        }
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

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> DomainCount(string sql, string value)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string> DomainText(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> DomainGuid(string sql, string value)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<DateTimeOffset> DomainTime(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        return await command.ExecuteScalarAsync() switch
        {
            DateTimeOffset offset => offset,
            DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
            _ => throw new InvalidOperationException("A timestamp was not returned.")
        };
    }
}
