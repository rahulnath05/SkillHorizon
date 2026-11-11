using Npgsql;
using SkillHorizon.Composition;
using SkillHorizon.Worker;
using SkillHorizon.Database;
using SkillHorizon.Hosting;
using SkillHorizon.Messaging;

var builder = WebApplication.CreateBuilder(args);
var options = builder.BindSkillHorizonOptions();
if (string.IsNullOrWhiteSpace(options.DomainConnectionString))
    throw new InvalidOperationException("SkillHorizon:DomainConnectionString is required.");

builder.AddSkillHorizonTelemetry("skillhorizon-worker");
builder.Services.AddSkillHorizonSecrets();
builder.Services.AddSkillHorizonZoneDirectory(options);
builder.Services.AddSingleton(services =>
    NpgsqlDataSource.Create(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SkillHorizonOptions>>().Value.DomainConnectionString));
builder.Services.AddSkillHorizonModules();
builder.Services.AddSkillHorizonMessaging(options);
builder.Services.AddHostedService<WorkforceSyncWorker>();
builder.Services.AddHostedService<ReferenceReplicationWorker>();
builder.Services.AddHostedService<ReferenceFanoutWorker>();
builder.Services.AddHostedService<ReferenceInboxWorker>();
builder.Services.AddHostedService<OutboxPublishWorker>();
builder.Services.AddHostedService<PracticeProjectionWorker>();
builder.Services.AddHealthChecks()
    .AddNpgSql(options.DomainConnectionString, name: "domain-postgres")
    .AddRedis(options.Redis, name: "redis");

var app = builder.Build();
if (options.MigrateOnStartup)
    SchemaMigrator.ApplyDomain(options.DomainConnectionString);

app.UseSkillHorizonRequestLogging();
app.MapHealthChecks("/health");
app.Run();

public partial class Program;
