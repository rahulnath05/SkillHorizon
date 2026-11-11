extern alias ApiHost;
extern alias ControlHost;
extern alias LearningHost;
extern alias WebHost;
extern alias WorkerHost;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SkillHorizon.Hosting;
using WebShell = WebHost::SkillHorizon.Web;

namespace SkillHorizon.Framework.Tests;

public sealed class FrameworkInfrastructure : IAsyncLifetime
{
    public const string SigningKey = "local-signing-key-must-be-at-least-32b";
    public const string RegistryKey = "local-registry-key";
    public const string DevIdentities =
        "person-multi|LE-LOCAL|person-multi;person-single|LE-LOCAL|person-single;person-unmapped|LE-OTHER|person-unmapped;person-nogrant|LE-LOCAL|person-nogrant;person-ld|LE-LOCAL|person-ld";

    private Testcontainers.PostgreSql.PostgreSqlContainer? _postgres;
    private Testcontainers.Redis.RedisContainer? _redis;

    public const string LearningKey = "local-learning-key";

    public string DomainConnection { get; private set; } = "";
    public string ControlConnection { get; private set; } = "";
    public string AssessmentConnection { get; private set; } = "";
    public string ProgressConnection { get; private set; } = "";
    public string RedisConnection { get; private set; } = "";

    public ApiFactory Api { get; private set; } = null!;
    public ControlFactory Control { get; private set; } = null!;
    public LearningFactory Learning { get; private set; } = null!;
    public WorkerFactory Worker { get; private set; } = null!;
    public WebFactory Web { get; private set; } = null!;
    public LogCapture Logs { get; } = new();

    public async Task InitializeAsync()
    {
        var configuredDomain = Environment.GetEnvironmentVariable("SKILLHORIZON_TEST_DOMAIN");
        var configuredRedis = Environment.GetEnvironmentVariable("SKILLHORIZON_TEST_REDIS");
        if (!string.IsNullOrWhiteSpace(configuredDomain) && !string.IsNullOrWhiteSpace(configuredRedis))
        {
            DomainConnection = configuredDomain;
            RedisConnection = configuredRedis;
        }
        else
        {
            _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("skillhorizon_domain")
                .WithUsername("skillhorizon")
                .WithPassword("skillhorizon")
                .Build();
            _redis = new Testcontainers.Redis.RedisBuilder("redis:7-alpine").Build();
            await _postgres.StartAsync();
            await _redis.StartAsync();
            DomainConnection = _postgres.GetConnectionString();
            RedisConnection = _redis.GetConnectionString();
        }

        var controlBuilder = new NpgsqlConnectionStringBuilder(DomainConnection) { Database = "skillhorizon_control" };
        await using (var connection = new NpgsqlConnection(DomainConnection))
        {
            await connection.OpenAsync();
            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = 'skillhorizon_control'", connection);
            if (await exists.ExecuteScalarAsync() is null)
            {
                await using var create = new NpgsqlCommand("CREATE DATABASE skillhorizon_control", connection);
                await create.ExecuteNonQueryAsync();
            }
        }

        ControlConnection = controlBuilder.ConnectionString;
        AssessmentConnection = await EnsureDatabase("skillhorizon_assessment");
        ProgressConnection = await EnsureDatabase("skillhorizon_progress");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        ApplyProcessConfiguration("control");
        Control = new ControlFactory(this);
        using (var _ = Control.CreateClient())
        {
        }

        ApplyProcessConfiguration("local");
        Api = new ApiFactory(this);
        using (var _ = Api.CreateClient())
        {
        }

        Worker = new WorkerFactory(this);
        using (var _ = Worker.CreateClient())
        {
        }

        Learning = new LearningFactory(this);
        using (var _ = Learning.CreateClient())
        {
        }

        Web = new WebFactory(this);
        using (var _ = Web.CreateClient())
        {
        }

        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (Web is not null)
            await Web.DisposeAsync();
        if (Learning is not null)
            await Learning.DisposeAsync();
        if (Worker is not null)
            await Worker.DisposeAsync();
        if (Api is not null)
            await Api.DisposeAsync();
        if (Control is not null)
            await Control.DisposeAsync();
        if (_redis is not null)
            await _redis.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    public async Task<string> IssueTokenAsync(string subjectId, string legalEntityId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"SkillHorizon:Secrets:local-signing-key"] = SigningKey
            })
            .Build();
        var provider = new LocalDevelopmentIdentityProvider(
            Microsoft.Extensions.Options.Options.Create(new SkillHorizonOptions
            {
                SigningKeySecretName = "local-signing-key"
            }),
            new ConfigurationSecretStore(configuration));
        return await provider.IssueTokenAsync(new IdentityAssertion(subjectId, subjectId, legalEntityId), CancellationToken.None);
    }

    private async Task SeedAsync()
    {
        await using (var control = new NpgsqlConnection(ControlConnection))
        {
            await control.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO zone.zone (zone_id, display_name, status, api_base_url, registered_at)
                VALUES ('local', 'Local zone', 'active', 'http://api:8080', now())
                ON CONFLICT (zone_id) DO NOTHING;

                INSERT INTO routing.legal_entity (legal_entity_id, zone_id, display_name)
                VALUES ('LE-LOCAL', 'local', 'Local employing entity')
                ON CONFLICT (legal_entity_id) DO NOTHING;
                """,
                control);
            await command.ExecuteNonQueryAsync();
        }

        await using var domain = new NpgsqlConnection(DomainConnection);
        await domain.OpenAsync();
        await using var grants = new NpgsqlCommand(
            """
            INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
            VALUES
                ('11111111-1111-1111-1111-111111111111', 'person-multi', 'employee', now()),
                ('22222222-2222-2222-2222-222222222222', 'person-multi', 'people_manager', now()),
                ('33333333-3333-3333-3333-333333333333', 'person-single', 'employee', now()),
                ('44444444-4444-4444-4444-444444444444', 'person-ld', 'learning_and_development', now())
            ON CONFLICT (subject_id, workspace) DO NOTHING;
            """,
            domain);
        await grants.ExecuteNonQueryAsync();
    }

    private async Task<string> EnsureDatabase(string database)
    {
        await using var connection = new NpgsqlConnection(DomainConnection);
        await connection.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = $1", connection);
        exists.Parameters.AddWithValue(database);
        if (await exists.ExecuteScalarAsync() is null)
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(DomainConnection) { Database = database };
        return builder.ConnectionString;
    }

    private void ApplyProcessConfiguration(string zoneId)
    {
        Environment.SetEnvironmentVariable("SkillHorizon__ZoneId", zoneId);
        Environment.SetEnvironmentVariable("SkillHorizon__DomainConnectionString", DomainConnection);
        Environment.SetEnvironmentVariable("SkillHorizon__ControlConnectionString", ControlConnection);
        Environment.SetEnvironmentVariable("SkillHorizon__Redis", RedisConnection);
        Environment.SetEnvironmentVariable("SkillHorizon__ControlRegistryUrl", "http://control");
        Environment.SetEnvironmentVariable("SkillHorizon__ApiUrl", "http://api");
        Environment.SetEnvironmentVariable("SkillHorizon__EnableConsoleExporter", "false");
        Environment.SetEnvironmentVariable("SkillHorizon__MigrateOnStartup", "true");
        Environment.SetEnvironmentVariable("SkillHorizon__PublishOutbox", "false");
        Environment.SetEnvironmentVariable("SkillHorizon__SyncWorkforceOnStartup", "false");
        Environment.SetEnvironmentVariable("SkillHorizon__LoadStandardsFixture", "false");
        Environment.SetEnvironmentVariable("SkillHorizon__LoadLearningFixture", "false");
        Environment.SetEnvironmentVariable("SkillHorizon__ReplicateReference", "false");
        Environment.SetEnvironmentVariable("SkillHorizon__AssessmentConnectionString", AssessmentConnection);
        Environment.SetEnvironmentVariable("SkillHorizon__ProgressConnectionString", ProgressConnection);
        Environment.SetEnvironmentVariable("SkillHorizon__EvidenceRoot", Path.Combine(Path.GetTempPath(), "skillhorizon-evidence-tests"));
        Environment.SetEnvironmentVariable("SkillHorizon__LearningUrl", "http://learning");
        Environment.SetEnvironmentVariable("SkillHorizon__LearningServiceKeySecretName", "learning-service-key");
        Environment.SetEnvironmentVariable("SkillHorizon__Secrets__learning-service-key", LearningKey);
        Environment.SetEnvironmentVariable("SkillHorizon__SigningKeySecretName", "local-signing-key");
        Environment.SetEnvironmentVariable("SkillHorizon__RegistryKeySecretName", "registry-key");
        Environment.SetEnvironmentVariable("SkillHorizon__DevIdentities", DevIdentities);
        Environment.SetEnvironmentVariable("SkillHorizon__Secrets__local-signing-key", SigningKey);
        Environment.SetEnvironmentVariable("SkillHorizon__Secrets__registry-key", RegistryKey);
    }
}

public sealed class ApiFactory : WebApplicationFactory<ApiHost::Program>
{
    private readonly FrameworkInfrastructure _infrastructure;

    public ApiFactory(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Shared(_infrastructure)));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(new CaptureLoggerProvider(_infrastructure.Logs));
            services.AddSingleton<IZoneDirectory>(sp =>
            {
                var handler = _infrastructure.Control.Server.CreateHandler();
                var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://control/") };
                return new ControlRegistryClient(
                    http,
                    sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                    sp.GetRequiredService<ISecretStore>(),
                    Microsoft.Extensions.Options.Options.Create(new SkillHorizonOptions
                    {
                        RegistryKeySecretName = "registry-key",
                        ZoneId = "local"
                    }));
            });
        });
    }

    internal static Dictionary<string, string?> Shared(FrameworkInfrastructure infrastructure) => new()
    {
        ["SkillHorizon:ZoneId"] = "local",
        ["SkillHorizon:DomainConnectionString"] = infrastructure.DomainConnection,
        ["SkillHorizon:Redis"] = infrastructure.RedisConnection,
        ["SkillHorizon:ControlRegistryUrl"] = "http://control",
        ["SkillHorizon:EnableConsoleExporter"] = "false",
        ["SkillHorizon:MigrateOnStartup"] = "true",
        ["SkillHorizon:PublishOutbox"] = "false",
        ["SkillHorizon:SyncWorkforceOnStartup"] = "false",
        ["SkillHorizon:LoadStandardsFixture"] = "false",
        ["SkillHorizon:LoadLearningFixture"] = "false",
        ["SkillHorizon:ReplicateReference"] = "false",
        ["SkillHorizon:LearningServiceKeySecretName"] = "learning-service-key",
        ["SkillHorizon:Secrets:learning-service-key"] = FrameworkInfrastructure.LearningKey,
        ["SkillHorizon:SigningKeySecretName"] = "local-signing-key",
        ["SkillHorizon:RegistryKeySecretName"] = "registry-key",
        ["SkillHorizon:Secrets:local-signing-key"] = FrameworkInfrastructure.SigningKey,
        ["SkillHorizon:Secrets:registry-key"] = FrameworkInfrastructure.RegistryKey,
        ["SkillHorizon:DevIdentities"] = FrameworkInfrastructure.DevIdentities
    };
}

public sealed class ControlFactory : WebApplicationFactory<ControlHost::Program>
{
    private readonly FrameworkInfrastructure _infrastructure;

    public ControlFactory(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
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
    }
}

public sealed class WorkerFactory : WebApplicationFactory<WorkerHost::Program>
{
    private readonly FrameworkInfrastructure _infrastructure;

    public WorkerFactory(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(ApiFactory.Shared(_infrastructure)));
    }
}

public sealed class WebFactory : WebApplicationFactory<WebHost::Program>
{
    private readonly FrameworkInfrastructure _infrastructure;

    public WebFactory(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        var settings = ApiFactory.Shared(_infrastructure);
        settings["SkillHorizon:ApiUrl"] = "http://api";
        settings["SkillHorizon:LearningUrl"] = "http://learning";
        settings["SkillHorizon:ZoneId"] = "local";
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<WebShell.CoreApiClient>(_ =>
            {
                var handler = _infrastructure.Api.Server.CreateHandler();
                var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://api/") };
                return new WebShell.CoreApiClient(http);
            });
            services.AddSingleton<WebShell.LearningApiClient>(_ =>
            {
                var handler = _infrastructure.Learning.Server.CreateHandler();
                var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://learning/") };
                return new WebShell.LearningApiClient(http);
            });
        });
    }
}

public sealed class LearningFactory : WebApplicationFactory<LearningHost::Program>
{
    private readonly FrameworkInfrastructure _infrastructure;

    public LearningFactory(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SkillHorizon:ZoneId"] = "local",
            ["SkillHorizon:ApiUrl"] = "http://api",
            ["SkillHorizon:AssessmentConnectionString"] = _infrastructure.AssessmentConnection,
            ["SkillHorizon:ProgressConnectionString"] = _infrastructure.ProgressConnection,
            ["SkillHorizon:EnableConsoleExporter"] = "false",
            ["SkillHorizon:MigrateOnStartup"] = "true",
            ["SkillHorizon:LoadLearningFixture"] = "false",
            ["SkillHorizon:EvidenceRoot"] = Path.Combine(Path.GetTempPath(), "skillhorizon-evidence-tests"),
            ["SkillHorizon:LearningServiceKeySecretName"] = "learning-service-key",
            ["SkillHorizon:Secrets:learning-service-key"] = FrameworkInfrastructure.LearningKey
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(new CaptureLoggerProvider(_infrastructure.Logs));
            services.AddSingleton<LearningHost::CoreCaller>(_ =>
            {
                var handler = _infrastructure.Api.Server.CreateHandler();
                var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://api/") };
                return new LearningHost::CoreCaller(http);
            });
        });
    }
}

[CollectionDefinition(Name)]
public sealed class FrameworkCollection : ICollectionFixture<FrameworkInfrastructure>
{
    public const string Name = "framework";
}
