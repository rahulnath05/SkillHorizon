using Npgsql;
using SkillHorizon.Access;
using SkillHorizon.Api;
using SkillHorizon.Composition;
using SkillHorizon.Database;
using SkillHorizon.Hosting;
using SkillHorizon.Messaging;

var builder = WebApplication.CreateBuilder(args);
var options = builder.BindSkillHorizonOptions();
if (string.IsNullOrWhiteSpace(options.DomainConnectionString))
    throw new InvalidOperationException("SkillHorizon:DomainConnectionString is required.");

builder.AddSkillHorizonTelemetry("skillhorizon-api");
builder.Services.AddSkillHorizonSecrets();
builder.Services.AddSkillHorizonIdentity(options);
builder.Services.AddSkillHorizonZoneDirectory(options);
builder.Services.AddSingleton(services =>
    NpgsqlDataSource.Create(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SkillHorizonOptions>>().Value.DomainConnectionString));
builder.Services.AddSkillHorizonModules();
builder.Services.AddSkillHorizonMessaging(options);
builder.Services.AddSingleton<RequireGrantFilter>();
builder.Services.AddSingleton<StandardsFixture>();
builder.Services.AddSingleton<LearningAdmissionFixture>();
builder.Services.AddSingleton<PersonaContentFixture>();
builder.Services.AddHealthChecks()
    .AddNpgSql(options.DomainConnectionString, name: "domain-postgres")
    .AddRedis(options.Redis, name: "redis");

var app = builder.Build();
_ = app.Services.GetRequiredService<IIdentityProvider>();
if (options.MigrateOnStartup)
    SchemaMigrator.ApplyDomain(options.DomainConnectionString);

app.UseSkillHorizonRequestLogging();
app.UseSkillHorizonAuthentication();
app.UseSkillHorizonZoneGate();
app.MapHealthChecks("/health");
app.MapWorkforce();
app.MapManager();
app.MapStandards();
app.MapCatalogue();
app.MapLearning();
app.MapPractice();
app.MapAggregates();
app.MapAdmin();
if (options.LoadStandardsFixture)
    app.Services.GetRequiredService<StandardsFixture>().LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
app.Services.GetRequiredService<LearningAdmissionFixture>().LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
if (options.LoadLearningFixture)
    app.Services.GetRequiredService<PersonaContentFixture>().LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
if (args.Contains("--seed-only"))
    return;
app.MapGet("/access/workspaces", async (HttpContext context, IGrantCheck grants, CancellationToken cancellationToken) =>
{
    var subject = context.User.FindFirst("sub")?.Value ?? "";
    var workspaces = await grants.ListWorkspacesAsync(subject, cancellationToken).ConfigureAwait(false);
    return Results.Ok(new { workspaces });
});
app.MapPost("/commands/noop", () => Results.NoContent()).AddEndpointFilter<RequireGrantFilter>();
app.Run();

public partial class Program;
