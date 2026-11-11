extern alias ApiHost;
extern alias ControlHost;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SkillHorizon.Benchmark;
using SkillHorizon.Database;
using SkillHorizon.Hosting;
using SkillHorizon.Messaging;
using SkillHorizon.Taxonomy;
using SkillHorizon.Workforce;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class TerritoryProofTests
{
    private const string Prompt = "NORTH-ONLY-ITEM-TEXT";
    private const string Practice = "territory-practice";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly FrameworkInfrastructure _infrastructure;

    public TerritoryProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task A_territory_keeps_its_own_data_and_the_cross_zone_figure_is_a_sum()
    {
        var draftId = Guid.Empty;
        ZoneApiFactory? northApi = null;
        ZoneApiFactory? southApi = null;
        AggregateControlFactory? aggregateControl = null;
        try
        {
            var northDomain = await EnsureDatabase("skillhorizon_north_domain");
            var northAssessment = await EnsureDatabase("skillhorizon_north_assessment");
            var southDomain = await EnsureDatabase("skillhorizon_south_domain");
            var southAssessment = await EnsureDatabase("skillhorizon_south_assessment");
            await EnsureDatabase("skillhorizon_north_progress");
            await EnsureDatabase("skillhorizon_south_progress");
            SchemaMigrator.ApplyDomain(northDomain);
            SchemaMigrator.ApplyAssessment(northAssessment);
            SchemaMigrator.ApplyDomain(southDomain);
            SchemaMigrator.ApplyAssessment(southAssessment);

            using var control = _infrastructure.Control.CreateClient();
            control.DefaultRequestHeaders.TryAddWithoutValidation("X-Registry-Key", FrameworkInfrastructure.RegistryKey);
            await Accept(control, "/zones", new { zoneId = "north", displayName = "North residency", status = "provisioning", apiBaseUrl = "http://north", queueUrl = "" }, HttpStatusCode.Created, HttpStatusCode.Conflict);
            await Accept(control, "/zones", new { zoneId = "south", displayName = "South residency", status = "provisioning", apiBaseUrl = "http://south", queueUrl = "" }, HttpStatusCode.Created, HttpStatusCode.Conflict);
            await Accept(control, "/routing/legal-entities", new { legalEntityId = "LE-NORTH", zoneId = "north", displayName = "North employing entity" }, HttpStatusCode.Created, HttpStatusCode.Conflict);
            await Accept(control, "/routing/legal-entities", new { legalEntityId = "LE-SOUTH", zoneId = "south", displayName = "South employing entity" }, HttpStatusCode.Created, HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.NotFound, (await control.GetAsync("/routing/legal-entities/LE-OTHER")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await control.PostAsJsonAsync("/zones/north/activate", new { })).StatusCode);
            await Accept(control, "/zones/north/reference", new { enabled = true }, HttpStatusCode.NoContent);
            await Accept(control, "/zones/north/hr", new { enabled = true }, HttpStatusCode.NoContent);
            await Accept(control, "/zones/north/activate", new { }, HttpStatusCode.NoContent);
            await Accept(control, "/zones/south/reference", new { enabled = true }, HttpStatusCode.NoContent);
            await Accept(control, "/zones/south/hr", new { enabled = true }, HttpStatusCode.NoContent);
            await Accept(control, "/zones/south/activate", new { }, HttpStatusCode.NoContent);
            await Accept(control, "/zones/south/reference", new { enabled = false }, HttpStatusCode.NoContent);

            var refused = await _infrastructure.Api.CreateClient().SendAsync(Authorized("/access/workspaces", "person-north", "LE-NORTH"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("another zone", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var localSync = _infrastructure.Api.Services.GetRequiredService<IWorkforceSync>();
            var projected = await localSync.ApplyAsync(People(), CancellationToken.None);
            Assert.Equal(1, projected.Projected);
            Assert.NotNull(await Person(_infrastructure.DomainConnection, "territory-local"));
            Assert.Null(await Person(_infrastructure.DomainConnection, "territory-north"));
            Assert.Null(await Person(_infrastructure.DomainConnection, "territory-other"));

            var placement = new SkillHorizon.Composition.RegistryHrPlacement(
                Directory("north"),
                Options.Create(new SkillHorizonOptions { ZoneId = "north", RegistryKeySecretName = "registry-key" }));
            await using var northData = NpgsqlDataSource.Create(northDomain);
            var northSync = new PostgresWorkforceSync(northData, new FixtureHrFeed(), placement);
            var northProjected = await northSync.ApplyAsync(People(), CancellationToken.None);
            Assert.Equal(1, northProjected.Projected);
            Assert.Equal("LE-NORTH", await Person(northDomain, "territory-north"));
            Assert.Null(await Person(northDomain, "territory-local"));
            Assert.Null(await Person(northDomain, "territory-other"));
            Assert.Null(await Person(southDomain, "territory-other"));

            draftId = await AuthoringDraft();
            var taxonomyId = Guid.NewGuid();
            var benchmarkId = Guid.NewGuid();
            var skillId = Guid.NewGuid();
            await InsertPublication(taxonomyId, "taxonomy.published", JsonSerializer.Serialize(new TaxonomyPublication(
                "taxonomy.published",
                taxonomyId,
                7,
                DateTimeOffset.UtcNow,
                null,
                [new PublishedSkill(skillId, "territory-skill", "Territory skill", "analysis", "Analysis", "territory", "Territory")],
                [new PublishedProficiency("beginner", "Beginner", 1)]), Json));
            await InsertPublication(benchmarkId, "benchmark.published", JsonSerializer.Serialize(new BenchmarkPublication(
                "benchmark.published",
                benchmarkId,
                Guid.NewGuid(),
                "territory-profile",
                "Territory profile",
                "consulting",
                "Consulting",
                "associate",
                "Associate",
                1,
                taxonomyId,
                1,
                DateTimeOffset.UtcNow,
                null,
                [new PublishedTarget(skillId, "territory-skill", "Territory skill", "Analysis", "Territory", 1, "beginner", "Beginner")]), Json));

            var distributor = _infrastructure.Api.Services.GetRequiredService<ReferenceDistributor>();
            var subscribers = await Directory("local").ReferenceSubscribersAsync(CancellationToken.None);
            Assert.Contains(subscribers, subscriber => subscriber.ZoneId == "north");
            Assert.DoesNotContain(subscribers, subscriber => subscriber.ZoneId == "south");
            var delivered = await distributor.DeliverAsync(
                subscribers.Select(subscriber => new ReferenceDestination(subscriber.ZoneId, subscriber.QueueUrl)).ToArray(),
                CancellationToken.None);
            Assert.True(delivered >= 2);
            var northLibrary = new PostgresReferenceLibrary(northData);
            var received = await distributor.ReceiveAsync(
                "north",
                (id, type, payload, token) => northLibrary.ApplyPublicationAsync(id, type, payload, token),
                CancellationToken.None);
            Assert.True(received >= 2);
            Assert.Equal(1, await Count(northDomain, "SELECT count(*) FROM reference.taxonomy_version WHERE taxonomy_version_id = $1", taxonomyId));
            Assert.Equal(1, await Count(northDomain, "SELECT count(*) FROM reference.benchmark_version WHERE benchmark_version_id = $1", benchmarkId));
            Assert.Equal(0, await Count(northDomain, "SELECT count(*) FROM reference.taxonomy_version WHERE taxonomy_version_id = $1", draftId));
            Assert.Equal(0, await Count(northDomain, "SELECT count(*) FROM taxonomy.version"));
            Assert.Equal(0, await Count(southDomain, "SELECT count(*) FROM reference.taxonomy_version"));
            Assert.Equal(0, await Count(southDomain, "SELECT count(*) FROM reference.benchmark_version"));

            await InsertSnapshot(northDomain, 2, 1, 1, 1);
            await InsertSnapshot(southDomain, 5, 4, 3, 2);
            await InsertResponse(northAssessment);

            northApi = new ZoneApiFactory(ZoneSettings("north", northDomain));
            southApi = new ZoneApiFactory(ZoneSettings("south", southDomain));
            using var northClient = northApi.CreateClient();
            using var southClient = southApi.CreateClient();
            northClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Registry-Key", FrameworkInfrastructure.RegistryKey);
            southClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Registry-Key", FrameworkInfrastructure.RegistryKey);
            var northBody = await northClient.GetStringAsync("/aggregates/practice?practice=" + Practice);
            var southBody = await southClient.GetStringAsync("/aggregates/practice?practice=" + Practice);
            Assert.Equal(2, JsonSerializer.Deserialize<JsonElement>(northBody).GetProperty("memberCount").GetInt32());
            Assert.Equal(5, JsonSerializer.Deserialize<JsonElement>(southBody).GetProperty("memberCount").GetInt32());
            Assert.DoesNotContain(Prompt, northBody, StringComparison.Ordinal);
            Assert.DoesNotContain("territory-north", northBody, StringComparison.Ordinal);

            aggregateControl = new AggregateControlFactory(_infrastructure, northApi.Server.CreateHandler(), southApi.Server.CreateHandler(), _infrastructure.Api.Server.CreateHandler());
            using var aggregateClient = aggregateControl.CreateClient();
            aggregateClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Registry-Key", FrameworkInfrastructure.RegistryKey);
            var aggregate = await aggregateClient.GetStringAsync("/aggregates/practice?practice=" + Practice);
            var summed = JsonSerializer.Deserialize<JsonElement>(aggregate);
            Assert.Equal(7, summed.GetProperty("memberCount").GetInt32());
            Assert.Equal(5, summed.GetProperty("measuredCount").GetInt32());
            Assert.Equal(4, summed.GetProperty("gapCount").GetInt32());
            Assert.Equal(3, summed.GetProperty("cohortCount").GetInt32());
            Assert.DoesNotContain(Prompt, aggregate, StringComparison.Ordinal);
            Assert.DoesNotContain("territory-north", aggregate, StringComparison.Ordinal);
            Assert.Equal(1, await Count(northAssessment, "SELECT count(*) FROM assessment.item WHERE prompt = $1", Prompt));
            Assert.Equal(0, await Count(southAssessment, "SELECT count(*) FROM assessment.item WHERE prompt = $1", Prompt));
            Assert.Equal(0, await Count(_infrastructure.AssessmentConnection, "SELECT count(*) FROM assessment.item WHERE prompt = $1", Prompt));
            Assert.Equal(0, await Count(northDomain, "SELECT count(*) FROM workforce.person WHERE display_name = $1", Prompt));
            Assert.Equal(0, await Count(_infrastructure.DomainConnection, "SELECT count(*) FROM workforce.person WHERE display_name = $1", Prompt));
        }
        finally
        {
            aggregateControl?.Dispose();
            southApi?.Dispose();
            northApi?.Dispose();
            await DeletePeople();
            await DeleteDraft();
        }
    }

    private HttpRequestMessage Authorized(string path, string subject, string legalEntity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _infrastructure.IssueTokenAsync(subject, legalEntity).GetAwaiter().GetResult());
        return request;
    }

    private static async Task Accept(HttpClient client, string path, object body, params HttpStatusCode[] expected)
    {
        var response = await client.PostAsJsonAsync(path, body);
        if (!expected.Contains(response.StatusCode))
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(path + " returned " + (int)response.StatusCode + " " + text);
        }
    }

    private static List<HrRecord> People() =>
    [
        new("territory-local", "LE-LOCAL", "IN", null, "active", false, "territory-local"),
        new("territory-north", "LE-NORTH", "IN", null, "active", false, "territory-north"),
        new("territory-other", "LE-OTHER", "IN", null, "active", false, "territory-other")
    ];

    private IZoneDirectory Directory(string zoneId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SkillHorizon:Secrets:registry-key"] = FrameworkInfrastructure.RegistryKey
            })
            .Build();
        var http = new HttpClient(_infrastructure.Control.Server.CreateHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://control/")
        };
        return new ControlRegistryClient(
            http,
            new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationSecretStore(configuration),
            Options.Create(new SkillHorizonOptions { ZoneId = zoneId, RegistryKeySecretName = "registry-key" }));
    }

    private async Task<string> EnsureDatabase(string database)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = $1", connection);
        exists.Parameters.AddWithValue(database);
        if (await exists.ExecuteScalarAsync() is null)
        {
            await using var create = new NpgsqlCommand("CREATE DATABASE " + database, connection);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_infrastructure.DomainConnection) { Database = database };
        return builder.ConnectionString;
    }

    private async Task<Guid> AuthoringDraft()
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using (var read = new NpgsqlCommand("SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'draft'", connection))
        {
            if (await read.ExecuteScalarAsync() is Guid existing)
                return existing;
        }

        var id = Guid.NewGuid();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO taxonomy.version (taxonomy_version_id, version_number, status, published_at, created_at)
            VALUES ($1, 900, 'draft', NULL, now())
            """,
            connection);
        insert.Parameters.AddWithValue(id);
        await insert.ExecuteNonQueryAsync();
        return id;
    }

    private async Task InsertPublication(Guid id, string type, string payload)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO messaging.outbox (outbox_id, zone_id, message_type, payload, correlation_id, occurred_at)
            VALUES ($1, 'local', $2, $3, 'territory-proof', now())
            """,
            connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(type);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb, Value = payload });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSnapshot(string connectionString, int members, int measured, int gaps, int cohorts)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var clear = new NpgsqlCommand("DELETE FROM readiness.snapshot WHERE subject_key = $1", connection))
        {
            clear.Parameters.AddWithValue(Practice);
            await clear.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO readiness.snapshot (
                snapshot_id, subject_kind, subject_key, as_of, dirty,
                member_count, measured_count, gap_count, cohort_count)
            VALUES (gen_random_uuid(), 'practice', $1, now(), false, $2, $3, $4, $5)
            """,
            connection);
        command.Parameters.AddWithValue(Practice);
        command.Parameters.AddWithValue(members);
        command.Parameters.AddWithValue(measured);
        command.Parameters.AddWithValue(gaps);
        command.Parameters.AddWithValue(cohorts);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertResponse(string connectionString)
    {
        var itemId = Guid.NewGuid();
        var sittingId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM assessment.response WHERE skill_code = 'north-only'",
            "DELETE FROM assessment.sitting WHERE token_hash = 'territory-proof'",
            "DELETE FROM assessment.item WHERE skill_code = 'north-only'"
        })
        {
            await using var clear = new NpgsqlCommand(sql, connection);
            await clear.ExecuteNonQueryAsync();
        }

        await using var item = new NpgsqlCommand(
            "INSERT INTO assessment.item (item_id, skill_code, prompt) VALUES ($1, 'north-only', $2)",
            connection);
        item.Parameters.AddWithValue(itemId);
        item.Parameters.AddWithValue(Prompt);
        await item.ExecuteNonQueryAsync();
        await using var sitting = new NpgsqlCommand(
            """
            INSERT INTO assessment.sitting (
                sitting_id, authorization_id, token_hash, subject_id, wave_id, benchmark_version_id, scale, status, opened_at)
            VALUES ($1, $1, 'territory-proof', 'territory-north', $1, $1, '{}', 'open', now())
            """,
            connection);
        sitting.Parameters.AddWithValue(sittingId);
        await sitting.ExecuteNonQueryAsync();
        await using var response = new NpgsqlCommand(
            """
            INSERT INTO assessment.response (sitting_id, item_id, skill_code, selected_rank, responded_at)
            VALUES ($1, $2, 'north-only', 1, now())
            """,
            connection);
        response.Parameters.AddWithValue(sittingId);
        response.Parameters.AddWithValue(itemId);
        await response.ExecuteNonQueryAsync();
    }

    private async Task<string?> Person(string connectionString, string employeeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT legal_entity_id FROM workforce.person WHERE employee_id = $1",
            connection);
        command.Parameters.AddWithValue(employeeId);
        var value = await command.ExecuteScalarAsync();
        return value as string;
    }

    private static async Task<int> Count(string connectionString, string sql, object? value = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (value is not null)
            command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task DeletePeople()
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM workforce.management_relationship WHERE manager_employee_id IN ('territory-local', 'territory-north', 'territory-other') OR report_employee_id IN ('territory-local', 'territory-north', 'territory-other')",
            "DELETE FROM access.workspace_grant WHERE subject_id IN ('territory-local', 'territory-north', 'territory-other')",
            "DELETE FROM workforce.person WHERE employee_id IN ('territory-local', 'territory-north', 'territory-other')"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task DeleteDraft()
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM taxonomy.version WHERE version_number = 900 AND status = 'draft'", connection);
        await command.ExecuteNonQueryAsync();
    }

    private Dictionary<string, string?> ZoneSettings(string zoneId, string domain)
    {
        var settings = ApiFactory.Shared(_infrastructure);
        settings["SkillHorizon:ZoneId"] = zoneId;
        settings["SkillHorizon:DomainConnectionString"] = domain;
        settings["SkillHorizon:LoadStandardsFixture"] = "false";
        settings["SkillHorizon:LoadLearningFixture"] = "false";
        return settings;
    }

    private sealed class ZoneApiFactory : WebApplicationFactory<ApiHost::Program>
    {
        private readonly Dictionary<string, string?> _settings;

        public ZoneApiFactory(Dictionary<string, string?> settings)
        {
            _settings = settings;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(_settings);
            });
        }
    }

    private sealed class AggregateControlFactory : WebApplicationFactory<ControlHost::Program>
    {
        private readonly FrameworkInfrastructure _infrastructure;
        private readonly HttpMessageHandler _north;
        private readonly HttpMessageHandler _south;
        private readonly HttpMessageHandler _local;

        public AggregateControlFactory(
            FrameworkInfrastructure infrastructure,
            HttpMessageHandler north,
            HttpMessageHandler south,
            HttpMessageHandler local)
        {
            _infrastructure = infrastructure;
            _north = north;
            _south = south;
            _local = local;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SkillHorizon:ZoneId"] = "control",
                ["SkillHorizon:ControlConnectionString"] = _infrastructure.ControlConnection,
                ["SkillHorizon:EnableConsoleExporter"] = "false",
                ["SkillHorizon:MigrateOnStartup"] = "true",
                ["SkillHorizon:RegistryKeySecretName"] = "registry-key",
                ["SkillHorizon:Secrets:registry-key"] = FrameworkInfrastructure.RegistryKey
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient("zones")
                    .ConfigurePrimaryHttpMessageHandler(() => new SwitchHandler(_north, _south, _local));
            });
        }
    }

    private sealed class SwitchHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _north;
        private readonly HttpMessageInvoker _south;
        private readonly HttpMessageInvoker _local;

        public SwitchHandler(HttpMessageHandler north, HttpMessageHandler south, HttpMessageHandler local)
        {
            _north = new HttpMessageInvoker(north, disposeHandler: false);
            _south = new HttpMessageInvoker(south, disposeHandler: false);
            _local = new HttpMessageInvoker(local, disposeHandler: false);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? "";
            var invoker = host switch
            {
                "north" => _north,
                "south" => _south,
                "api" => _local,
                _ => throw new InvalidOperationException("No aggregate host for " + host + ".")
            };
            return invoker.SendAsync(request, cancellationToken);
        }
    }
}
