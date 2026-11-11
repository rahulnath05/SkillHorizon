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
public sealed class PracticePictureProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public PracticePictureProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task The_practice_picture_reads_snapshots_and_leaves_out_responses_and_evidence()
    {
        var india = "picture-in-" + _suffix;
        var singapore = "picture-sg-" + _suffix;
        var responseBody = "response-body-" + _suffix;
        var evidenceFile = "evidence-file-" + _suffix + ".pdf";
        await ClearDrafts();
        try
        {
            await Prove(india, singapore, responseBody, evidenceFile);
        }
        finally
        {
            await ExecuteProgress("DELETE FROM progress.evidence WHERE file_name = $1", evidenceFile);
            await ExecuteProgress("DELETE FROM progress.step WHERE subject_id = $1", india);
            await ExecuteProgress("DELETE FROM progress.path WHERE subject_id = $1", india);
            await ExecuteAssessment("DELETE FROM assessment.response WHERE sitting_id IN (SELECT sitting_id FROM assessment.sitting WHERE subject_id = $1)", india);
            await ExecuteAssessment("DELETE FROM assessment.sitting WHERE subject_id = $1", india);
            await ExecuteAssessment("DELETE FROM assessment.item WHERE prompt = $1", responseBody);
            await Execute("DELETE FROM messaging.reference_delivery WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM reference.applied_message WHERE outbox_id IN (SELECT outbox_id FROM messaging.outbox WHERE correlation_id LIKE $1)", "%" + _suffix + "%");
            await Execute("DELETE FROM messaging.outbox WHERE correlation_id LIKE $1", "%" + _suffix + "%");
            await Execute("DELETE FROM market.demand WHERE practice_code = $1", Code("practice"));
            await Execute("DELETE FROM reference.market_demand WHERE practice_code = $1", Code("practice"));
            await Execute("DELETE FROM attestation.cohort_member WHERE employee_id = $1", india);
            await Execute("DELETE FROM attestation.cohort_member WHERE employee_id = $1", singapore);
            await Execute("DELETE FROM attestation.cohort WHERE practice_code = $1", Code("practice"));
            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", india);
            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", singapore);
        }
    }

    private async Task Prove(string india, string singapore, string responseBody, string evidenceFile)
    {
        using var api = _infrastructure.Api.CreateClient();
        var jobs = _infrastructure.Api.Services.GetRequiredService<IJobArchitecture>();
        var taxonomy = _infrastructure.Api.Services.GetRequiredService<ITaxonomyAuthoring>();
        var benchmarks = _infrastructure.Api.Services.GetRequiredService<IBenchmarkAuthoring>();
        await jobs.ImportAsync(
            [new FrameworkImport(Code("field"), "Picture field",
                [new LevelImport(Code("apprentice"), "Apprentice", 1), new LevelImport(Code("specialist"), "Specialist", 2)],
                [
                    new ProfileImport(Code("high"), "High apprentice", Code("apprentice")),
                    new ProfileImport(Code("low"), "Low apprentice", Code("apprentice")),
                    new ProfileImport(Code("specialist"), "Specialist profile", Code("specialist"))
                ])],
            CancellationToken.None);
        var profiles = (await jobs.ListAsync(CancellationToken.None)).Single(framework => framework.Code == Code("field")).Profiles;
        var high = profiles.Single(profile => profile.Code == Code("high"));
        var low = profiles.Single(profile => profile.Code == Code("low"));
        var specialist = profiles.Single(profile => profile.Code == Code("specialist"));
        var drafted = await taxonomy.SaveDraftAsync(new TaxonomyDraft(
            [new PracticeDraft(Code("practice"), "Picture practice", [new CompetencyDraft(Code("competency"), "Picture competency", [new SkillDraft(Code("skill"), "Picture measure")])])],
            [new ProficiencyDraft(Code("held"), "Held", 1), new ProficiencyDraft(Code("reached"), "Reached", 2)]), CancellationToken.None);
        var publishedTaxonomy = await taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "picture-taxonomy-" + _suffix, CancellationToken.None);
        var skill = publishedTaxonomy.Skills.Single();
        await PublishBenchmark(benchmarks, high.ProfileId, publishedTaxonomy.TaxonomyVersionId, skill.SkillId, 2, "high");
        await PublishBenchmark(benchmarks, low.ProfileId, publishedTaxonomy.TaxonomyVersionId, skill.SkillId, 1, "low");
        await PublishBenchmark(benchmarks, specialist.ProfileId, publishedTaxonomy.TaxonomyVersionId, skill.SkillId, 2, "specialist");

        var submitted = await Send(api, HttpMethod.Post, "/commands/packs/submit", "person-ld", "learning_and_development", new
        {
            practiceCode = Code("practice"),
            rows = new object[]
            {
                new { jobProfileId = high.ProfileId, classification = "aligned" },
                new { jobProfileId = low.ProfileId, classification = "aligned" },
                new { jobProfileId = specialist.ProfileId, classification = "aligned" }
            }
        });
        var submittedBody = await submitted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var packId = JsonSerializer.Deserialize<JsonElement>(submittedBody).GetProperty("packId").GetGuid();
        var scope = await Send(api, HttpMethod.Post, "/commands/practice-scope", "person-ld", "learning_and_development", new
        {
            subjectId = "person-lead",
            practiceCode = Code("practice")
        });
        Assert.Equal(HttpStatusCode.NoContent, scope.StatusCode);
        var sealedPack = await Send(api, HttpMethod.Post, $"/commands/packs/{packId}/seal", "person-lead", "competency_lead", new { });
        Assert.Equal(HttpStatusCode.OK, sealedPack.StatusCode);

        await Person(india, "IN");
        await Person(singapore, "SG");
        var indiaCohort = await Send(api, HttpMethod.Post, "/commands/cohorts", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            countryCode = "IN",
            jobProfileId = high.ProfileId,
            memberIds = new[] { india }
        });
        Assert.Equal(HttpStatusCode.OK, indiaCohort.StatusCode);
        var singaporeCohort = await Send(api, HttpMethod.Post, "/commands/cohorts", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            countryCode = "SG",
            jobProfileId = specialist.ProfileId,
            memberIds = new[] { singapore }
        });
        Assert.Equal(HttpStatusCode.OK, singaporeCohort.StatusCode);

        var pathId = Guid.NewGuid();
        var openStep = Guid.NewGuid();
        await ExecuteProgress(
            """
            INSERT INTO progress.path (path_id, subject_id, result_id, benchmark_version_id, created_at)
            VALUES ($1, $2, $3, $4, now())
            """,
            pathId, india, Guid.NewGuid(), Guid.NewGuid());
        await ExecuteProgress(
            """
            INSERT INTO progress.step (step_id, subject_id, provider, title, status, recorded_at, path_id, skill_code)
            VALUES ($1, $2, 'external', 'Practice step', 'assigned', now(), $3, 'picture-skill')
            """,
            openStep, india, pathId);
        await ExecuteProgress(
            """
            INSERT INTO progress.step (step_id, subject_id, provider, title, status, recorded_at, path_id, skill_code)
            VALUES ($1, $2, 'external', 'Practice step done', 'completed', now(), $3, 'picture-skill')
            """,
            Guid.NewGuid(), india, pathId);
        await ExecuteProgress(
            """
            INSERT INTO progress.evidence (evidence_id, step_id, object_key, file_name, content_type, byte_length, recorded_at)
            VALUES ($1, $2, $3, $4, 'application/pdf', 12, now())
            """,
            Guid.NewGuid(), openStep, "objects/" + evidenceFile, evidenceFile);
        var itemId = Guid.NewGuid();
        var sittingId = Guid.NewGuid();
        await ExecuteAssessment(
            "INSERT INTO assessment.item (item_id, skill_code, prompt) VALUES ($1, $2, $3)",
            itemId, "item-" + _suffix, responseBody);
        await ExecuteAssessment(
            """
            INSERT INTO assessment.sitting (
                sitting_id, authorization_id, token_hash, subject_id, wave_id, benchmark_version_id, scale, status, opened_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb, 'open', now())
            """,
            sittingId, Guid.NewGuid(), "token-" + _suffix, india, Guid.NewGuid(), Guid.NewGuid(), "{\"responseBody\":\"" + responseBody + "\"}");
        await ExecuteAssessment(
            """
            INSERT INTO assessment.response (sitting_id, item_id, skill_code, selected_rank, responded_at)
            VALUES ($1, $2, $3, 1, now())
            """,
            sittingId, itemId, "item-" + _suffix);
        Assert.Equal(1, await CountAssessment("SELECT count(*) FROM assessment.item WHERE prompt = $1", responseBody));
        Assert.Equal(1, await CountProgress("SELECT count(*) FROM progress.evidence WHERE file_name = $1", evidenceFile));

        var projection = _infrastructure.Worker.Services.GetRequiredService<IPracticeProjection>();
        await projection.ProjectAsync(CancellationToken.None);

        var outside = await Send(api, HttpMethod.Get, "/practice/baseline?practice=other-" + _suffix, "person-lead", "competency_lead", null);
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        var baseline = await Read(api, "/practice/baseline?practice=" + Code("practice"));
        var progression = await Read(api, "/practice/progression?practice=" + Code("practice"));
        var readiness = await Read(api, "/practice/readiness?practice=" + Code("practice"));
        var market = await Read(api, "/practice/market?practice=" + Code("practice"));
        var aggregate = await Aggregate(Code("practice"));
        foreach (var body in new[] { baseline.Body, progression.Body, readiness.Body, market.Body, aggregate })
        {
            Assert.DoesNotContain(responseBody, body, StringComparison.Ordinal);
            Assert.DoesNotContain(evidenceFile, body, StringComparison.Ordinal);
            Assert.DoesNotContain(india, body, StringComparison.Ordinal);
            Assert.DoesNotContain(singapore, body, StringComparison.Ordinal);
        }

        Assert.False(string.IsNullOrWhiteSpace(baseline.Json.GetProperty("asOf").GetString()));
        var cells = baseline.Json.GetProperty("cells").EnumerateArray().ToArray();
        Assert.Equal(2, cells.Length);
        var apprentice = cells.Single(cell => cell.GetProperty("levelCode").GetString() == Code("apprentice"));
        Assert.Equal(2, apprentice.GetProperty("proficiencyRank").GetInt32());
        Assert.Equal("Reached", apprentice.GetProperty("proficiencyName").GetString());
        Assert.Equal("Picture measure", apprentice.GetProperty("skillName").GetString());
        var specialistCell = cells.Single(cell => cell.GetProperty("levelCode").GetString() == Code("specialist"));
        Assert.Equal(2, specialistCell.GetProperty("proficiencyRank").GetInt32());

        Assert.False(string.IsNullOrWhiteSpace(progression.Json.GetProperty("asOf").GetString()));
        Assert.Equal(1, progression.Json.GetProperty("pathCount").GetInt32());
        Assert.Equal(2, progression.Json.GetProperty("stepCount").GetInt32());
        Assert.Equal(1, progression.Json.GetProperty("openStepCount").GetInt32());

        var snapshot = readiness.Json.GetProperty("snapshot");
        Assert.Equal(2, snapshot.GetProperty("memberCount").GetInt32());
        Assert.Equal(snapshot.GetProperty("memberCount").GetInt32(), JsonSerializer.Deserialize<JsonElement>(aggregate).GetProperty("memberCount").GetInt32());
        Assert.Equal(snapshot.GetProperty("measuredCount").GetInt32(), JsonSerializer.Deserialize<JsonElement>(aggregate).GetProperty("measuredCount").GetInt32());
        Assert.Equal(snapshot.GetProperty("gapCount").GetInt32(), JsonSerializer.Deserialize<JsonElement>(aggregate).GetProperty("gapCount").GetInt32());
        Assert.Equal(snapshot.GetProperty("cohortCount").GetInt32(), JsonSerializer.Deserialize<JsonElement>(aggregate).GetProperty("cohortCount").GetInt32());
        Assert.DoesNotContain("talentGroups", aggregate, StringComparison.Ordinal);
        Assert.DoesNotContain("countries", aggregate, StringComparison.Ordinal);
        Assert.Equal(1, readiness.Json.GetProperty("countries").EnumerateArray().Single(cut => cut.GetProperty("key").GetString() == "IN").GetProperty("memberCount").GetInt32());
        Assert.Equal(1, readiness.Json.GetProperty("countries").EnumerateArray().Single(cut => cut.GetProperty("key").GetString() == "SG").GetProperty("memberCount").GetInt32());
        Assert.Equal(1, readiness.Json.GetProperty("talentGroups").EnumerateArray().Single(cut => cut.GetProperty("key").GetString() == "Apprentice").GetProperty("memberCount").GetInt32());
        Assert.Equal(1, readiness.Json.GetProperty("talentGroups").EnumerateArray().Single(cut => cut.GetProperty("key").GetString() == "Specialist").GetProperty("memberCount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(snapshot.GetProperty("asOf").GetString()));

        var publishedNote = "published-note-" + _suffix;
        var unsentNote = "unsent-note-" + _suffix;
        var leadDraft = await Send(api, HttpMethod.Post, "/commands/market/draft", "person-lead", "competency_lead", new
        {
            practiceCode = Code("practice"),
            note = publishedNote,
            postingCount = 4,
            roleCount = 2
        });
        Assert.Equal(HttpStatusCode.Forbidden, leadDraft.StatusCode);
        var draft = await Send(api, HttpMethod.Post, "/commands/market/draft", "person-ld", "learning_and_development", new
        {
            practiceCode = Code("practice"),
            note = publishedNote,
            postingCount = 4,
            roleCount = 2
        });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Equal(0, await CountText("SELECT count(*) FROM reference.market_demand WHERE practice_code = $1", Code("practice")));
        Assert.Equal(0, await CountText("SELECT count(*) FROM messaging.outbox WHERE message_type = 'market.published' AND payload::text LIKE $1", "%" + publishedNote + "%"));
        var hidden = await Read(api, "/practice/market?practice=" + Code("practice"));
        Assert.DoesNotContain(publishedNote, hidden.Body, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, hidden.Json.GetProperty("demand").ValueKind);

        var published = await Send(api, HttpMethod.Post, "/commands/market/publish", "person-ld", "learning_and_development", new { practiceCode = Code("practice") });
        var publishedBody = await published.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.DoesNotContain(india, publishedBody, StringComparison.Ordinal);
        Assert.Equal(0, await CountText("SELECT count(*) FROM reference.market_demand WHERE practice_code = $1", Code("practice")));
        Assert.Equal(0, await CountAll(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE ((table_schema = 'market' AND table_name = 'demand')
                OR (table_schema = 'reference' AND table_name = 'market_demand')
                OR (table_schema = 'readiness' AND table_name IN ('heatmap', 'heatmap_cell', 'progression', 'cut')))
              AND column_name IN ('employee_id', 'subject_id', 'person_id', 'file_name', 'response_body')
            """));
        var library = _infrastructure.Api.Services.GetRequiredService<IReferenceLibrary>();
        var applied = await library.ApplyPendingAsync(CancellationToken.None);
        Assert.True(applied >= 1);
        var shown = await Read(api, "/practice/market?practice=" + Code("practice"));
        Assert.Equal(publishedNote, shown.Json.GetProperty("demand").GetProperty("note").GetString());
        Assert.Equal(4, shown.Json.GetProperty("demand").GetProperty("postingCount").GetInt32());
        Assert.Equal(2, shown.Json.GetProperty("demand").GetProperty("roleCount").GetInt32());
        Assert.DoesNotContain(india, shown.Body, StringComparison.Ordinal);

        var edited = await Send(api, HttpMethod.Post, "/commands/market/draft", "person-ld", "learning_and_development", new
        {
            practiceCode = Code("practice"),
            note = unsentNote,
            postingCount = 9,
            roleCount = 3
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        await library.ApplyPendingAsync(CancellationToken.None);
        var held = await Read(api, "/practice/market?practice=" + Code("practice"));
        Assert.Equal(publishedNote, held.Json.GetProperty("demand").GetProperty("note").GetString());
        Assert.Equal(4, held.Json.GetProperty("demand").GetProperty("postingCount").GetInt32());
        Assert.DoesNotContain(unsentNote, held.Body, StringComparison.Ordinal);
        Assert.Equal(publishedNote, await ScalarText("SELECT note FROM reference.market_demand WHERE practice_code = $1", Code("practice")));
        Assert.Equal(unsentNote, await ScalarText("SELECT note FROM market.demand WHERE practice_code = $1", Code("practice")));
    }

    private async Task PublishBenchmark(IBenchmarkAuthoring benchmarks, Guid profileId, Guid taxonomyVersionId, Guid skillId, int rank, string name)
    {
        var draft = await benchmarks.SaveDraftAsync(new BenchmarkDraftWrite(profileId, taxonomyVersionId, [new TargetDraft(skillId, rank)]), CancellationToken.None);
        await benchmarks.PublishAsync(draft.BenchmarkVersionId, "person-ld", "picture-" + name + "-" + _suffix, CancellationToken.None);
    }

    private string Code(string name) => name + "-" + _suffix;

    private async Task<(string Body, JsonElement Json)> Read(HttpClient client, string path)
    {
        var response = await Send(client, HttpMethod.Get, path, "person-lead", "competency_lead", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (body, JsonSerializer.Deserialize<JsonElement>(body));
    }

    private async Task<string> Aggregate(string practice)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/aggregates/practice?practice=" + practice);
        request.Headers.TryAddWithoutValidation("X-Registry-Key", FrameworkInfrastructure.RegistryKey);
        using var client = _infrastructure.Api.CreateClient();
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body;
    }

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "picture-" + _suffix + "-" + Guid.NewGuid().ToString("N"));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task Person(string employeeId, string country)
    {
        await Execute(
            """
            INSERT INTO workforce.person (
                employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                personal_learning_assigned, display_name, projected_at)
            VALUES ($1, 'LE-LOCAL', $2, 'person-multi', 'active', true, $1, now())
            ON CONFLICT (employee_id) DO UPDATE SET employment_status = 'active', country_code = $2, closed_at = NULL
            """,
            employeeId,
            country);
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

    private Task ExecuteProgress(string sql, params object[] values) => ExecuteOn(_infrastructure.ProgressConnection, sql, values);

    private Task ExecuteAssessment(string sql, params object[] values) => ExecuteOn(_infrastructure.AssessmentConnection, sql, values);

    private static async Task ExecuteOn(string connectionString, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAll(string sql)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
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

    private static async Task<int> CountOn(string connectionString, string sql, string value)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private Task<int> CountProgress(string sql, string value) => CountOn(_infrastructure.ProgressConnection, sql, value);

    private Task<int> CountAssessment(string sql, string value) => CountOn(_infrastructure.AssessmentConnection, sql, value);

    private async Task<string> ScalarText(string sql, string value)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
