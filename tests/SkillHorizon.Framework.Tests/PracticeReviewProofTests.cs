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
public sealed class PracticeReviewProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public PracticeReviewProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task A_lead_reviews_the_open_pack_and_accepts_a_request_without_publishing()
    {
        const string report = "review-report";
        await ClearDrafts();
        try
        {
            await Prove(report);
        }
        finally
        {
            await Execute("DELETE FROM attestation.request_acceptance WHERE request_id IN (SELECT request_id FROM workforce.profile_correction WHERE report_employee_id = $1)", report + "-" + _suffix);
            await Execute("DELETE FROM workforce.profile_correction WHERE report_employee_id = $1", report + "-" + _suffix);
            await Execute("DELETE FROM workforce.profile_correction WHERE note = $1", "outside-request-" + _suffix);
            await Execute("DELETE FROM attestation.cohort_member WHERE cohort_id IN (SELECT cohort_id FROM attestation.cohort WHERE practice_code = $1)", "other-" + _suffix);
            await Execute("DELETE FROM attestation.cohort WHERE practice_code = $1", "other-" + _suffix);
            await Execute("DELETE FROM workforce.management_relationship WHERE report_employee_id = $1", report + "-" + _suffix);
            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", report + "-" + _suffix);
        }
    }

    private async Task Prove(string reportName)
    {
        var report = reportName + "-" + _suffix;
        var outsider = "review-outsider-" + _suffix;
        using var api = _infrastructure.Api.CreateClient();
        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        await jobs.ImportAsync(
            [new FrameworkImport(Code("field"), "Review field", [new LevelImport(Code("apprentice"), "Apprentice", 1)],
            [
                new ProfileImport(Code("steady"), "Steady reviewer", Code("apprentice")),
                new ProfileImport(Code("shifted"), "Shifted reviewer", Code("apprentice"))
            ])],
            CancellationToken.None);
        var profiles = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles;
        var steady = profiles.Single(profile => profile.Code == Code("steady"));
        var shifted = profiles.Single(profile => profile.Code == Code("shifted"));
        var drafted = await taxonomy.SaveDraftAsync(new TaxonomyDraft(
            [new PracticeDraft(Code("practice"), "Review practice", [new CompetencyDraft(Code("competency"), "Review competency", [new SkillDraft(Code("skill"), "Review measure")])])],
            [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]), CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "review-taxonomy-" + _suffix, CancellationToken.None);
        var skill = publishedTaxonomy.Skills.Single();
        var steadyDraft = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(steady.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 2)]), CancellationToken.None);
        var shiftedDraft = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(shifted.ProfileId, publishedTaxonomy.TaxonomyVersionId, [new TargetDraft(skill.SkillId, 2)]), CancellationToken.None);
        await benchmarks.PublishAsync(steadyDraft.BenchmarkVersionId, "person-ld", "review-steady-" + _suffix, CancellationToken.None);
        await benchmarks.PublishAsync(shiftedDraft.BenchmarkVersionId, "person-ld", "review-shifted-" + _suffix, CancellationToken.None);

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
        var submittedJson = JsonSerializer.Deserialize<JsonElement>(submittedBody);
        var packId = submittedJson.GetProperty("packId").GetGuid();
        var alignedId = submittedJson.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("classification").GetString() == "aligned").GetProperty("rowId").GetGuid();
        var exceptionId = submittedJson.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("classification").GetString() == "tightening").GetProperty("rowId").GetGuid();

        var scope = await Send(api, HttpMethod.Post, "/commands/practice-scope", "person-ld", "learning_and_development", new
        {
            subjectId = "person-lead",
            practiceCode = Code("practice")
        });
        Assert.Equal(HttpStatusCode.NoContent, scope.StatusCode);

        var mixed = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/reviews", "person-lead", "competency_lead", new { rowIds = new[] { alignedId, exceptionId } });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        Assert.Equal(0, await Count("SELECT count(*) FROM attestation.row_review WHERE row_id = $1", alignedId));
        var marked = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/reviews", "person-lead", "competency_lead", new { rowIds = new[] { alignedId } });
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        Assert.Equal(1, await Count("SELECT count(*) FROM attestation.row_review WHERE row_id = $1", alignedId));
        Assert.Equal(0, await Count("SELECT count(*) FROM attestation.row_review WHERE row_id = $1", exceptionId));
        var alignedDecision = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/decisions", "person-lead", "competency_lead", new { rowId = alignedId, decision = "No exception here" });
        Assert.Equal(HttpStatusCode.BadRequest, alignedDecision.StatusCode);

        await Person(report);
        await Execute(
            """
            INSERT INTO workforce.management_relationship (manager_employee_id, report_employee_id)
            VALUES ('person-multi', $1)
            ON CONFLICT DO NOTHING
            """,
            report);
        var cohort = await Send(api, HttpMethod.Post, "/commands/cohorts", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            countryCode = "IN",
            jobProfileId = steady.ProfileId,
            memberIds = new[] { report }
        });
        Assert.Equal(HttpStatusCode.OK, cohort.StatusCode);
        var filed = await Send(api, HttpMethod.Post, "/workforce/team/corrections", "person-multi", "people_manager", new
        {
            subjectId = report,
            note = "practice-request-" + _suffix
        });
        var filedBody = await filed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, filed.StatusCode);
        var requestId = JsonSerializer.Deserialize<JsonElement>(filedBody).GetProperty("requestId").GetGuid();

        await Execute(
            """
            INSERT INTO workforce.profile_correction (request_id, manager_employee_id, report_employee_id, note, recorded_at)
            VALUES ($1, 'person-multi', $2, $3, now())
            """,
            Guid.NewGuid(),
            outsider,
            "outside-request-" + _suffix);
        await Execute(
            """
            INSERT INTO attestation.cohort (cohort_id, practice_code, country_code, job_profile_id, lead_subject_id, created_at)
            VALUES ($1, $2, 'IN', $3, 'person-lead', now())
            """,
            Guid.NewGuid(),
            "other-" + _suffix,
            steady.ProfileId);
        var otherCohort = await ScalarGuid("SELECT cohort_id FROM attestation.cohort WHERE practice_code = $1", "other-" + _suffix);
        await Execute(
            "INSERT INTO attestation.cohort_member (cohort_id, employee_id) VALUES ($1, $2)",
            otherCohort,
            outsider);

        var projection = _infrastructure.Worker.Services.GetRequiredService<IPracticeProjection>();
        await projection.ProjectAsync(CancellationToken.None);
        var briefing = await Send(api, HttpMethod.Get, "/practice/briefing?practice=" + Code("practice"), "person-lead", "competency_lead", null);
        var briefingBody = await briefing.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, briefing.StatusCode);
        Assert.Contains(packId.ToString(), briefingBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("practice-request-" + _suffix, briefingBody, StringComparison.Ordinal);
        Assert.DoesNotContain("outside-request-" + _suffix, briefingBody, StringComparison.Ordinal);
        var shown = JsonSerializer.Deserialize<JsonElement>(briefingBody);
        Assert.Equal("submitted", shown.GetProperty("pack").GetProperty("status").GetString());
        Assert.True(shown.GetProperty("snapshot").GetProperty("memberCount").GetInt32() >= 1);
        var outsideScope = await Send(api, HttpMethod.Get, "/practice/briefing?practice=other-" + _suffix, "person-lead", "competency_lead", null);
        Assert.Equal(HttpStatusCode.Forbidden, outsideScope.StatusCode);

        var benchmarksBefore = await CountAll("SELECT count(*) FROM benchmark.version");
        var publishedBefore = await CountAll("SELECT count(*) FROM benchmark.version WHERE status = 'published'");
        var authorAccept = await Send(api, HttpMethod.Post, $"/practice/requests/{requestId}/accept", "person-ld", "learning_and_development", new { practiceCode = Code("practice") });
        Assert.Equal(HttpStatusCode.Forbidden, authorAccept.StatusCode);
        var accepted = await Send(api, HttpMethod.Post, $"/practice/requests/{requestId}/accept", "person-lead", "competency_lead", new { practiceCode = Code("practice") });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(benchmarksBefore, await CountAll("SELECT count(*) FROM benchmark.version"));
        Assert.Equal(publishedBefore, await CountAll("SELECT count(*) FROM benchmark.version WHERE status = 'published'"));
        Assert.Equal(0, await Count("SELECT count(*) FROM catalogue.profile_opening WHERE request_id = $1", requestId));
        Assert.Equal(1, await Count("SELECT count(*) FROM attestation.request_acceptance WHERE request_id = $1", requestId));

        var undecided = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-lead", "competency_lead", new { });
        Assert.Equal(HttpStatusCode.BadRequest, undecided.StatusCode);
        Assert.Equal(0, await CountText("SELECT count(*) FROM attestation.baseline WHERE practice_code = $1", Code("practice")));
        var decided = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/decisions", "person-lead", "competency_lead", new
        {
            rowId = exceptionId,
            decision = "Keep the tighter bar " + _suffix
        });
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        var sealedPack = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-lead", "competency_lead", new { });
        Assert.Equal(HttpStatusCode.OK, sealedPack.StatusCode);
        Assert.Equal(1, await CountText("SELECT count(*) FROM attestation.baseline WHERE practice_code = $1", Code("practice")));
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "review-" + Guid.NewGuid().ToString("N"));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task Person(string employeeId)
    {
        await Execute(
            """
            INSERT INTO workforce.person (
                employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                personal_learning_assigned, display_name, projected_at)
            VALUES ($1, 'LE-LOCAL', 'IN', 'person-multi', 'active', true, $1, now())
            ON CONFLICT (employee_id) DO UPDATE SET employment_status = 'active', closed_at = NULL
            """,
            employeeId);
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

    private async Task<int> CountText(string sql, string value)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountAll(string sql)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<Guid> ScalarGuid(string sql, string value)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }
}
