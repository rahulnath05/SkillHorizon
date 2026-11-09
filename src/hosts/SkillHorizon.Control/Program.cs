using System.Security.Cryptography;
using System.Text;
using Npgsql;
using SkillHorizon.Control;
using SkillHorizon.Database;
using SkillHorizon.Hosting;

var builder = WebApplication.CreateBuilder(args);
var options = builder.BindSkillHorizonOptions();
if (string.IsNullOrWhiteSpace(options.ControlConnectionString))
    throw new InvalidOperationException("SkillHorizon:ControlConnectionString is required.");

builder.AddSkillHorizonTelemetry("skillhorizon-control");
builder.Services.AddSkillHorizonSecrets();
var registryKey = new ConfigurationSecretStore(builder.Configuration)
    .GetRequiredAsync(options.RegistryKeySecretName, CancellationToken.None)
    .GetAwaiter()
    .GetResult();
builder.Services.AddSingleton(new RegistryKey(registryKey));
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(options.ControlConnectionString));
builder.Services.AddSingleton<ControlStore>();
builder.Services.AddHealthChecks()
    .AddNpgSql(options.ControlConnectionString, name: "control-postgres");

var app = builder.Build();
if (options.MigrateOnStartup)
    SchemaMigrator.ApplyControl(options.ControlConnectionString);

app.UseSkillHorizonRequestLogging();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next().ConfigureAwait(false);
        return;
    }

    var expected = context.RequestServices.GetRequiredService<RegistryKey>().Value;
    var provided = context.Request.Headers[SkillHorizonContext.RegistryKeyHeader].ToString();
    var left = Encoding.UTF8.GetBytes(provided);
    var right = Encoding.UTF8.GetBytes(expected);
    if (left.Length != right.Length || !CryptographicOperations.FixedTimeEquals(left, right))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next().ConfigureAwait(false);
});

app.MapHealthChecks("/health");
app.MapGet("/zones", async (ControlStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.ListZonesAsync(cancellationToken).ConfigureAwait(false)));
app.MapGet("/routing/legal-entities/{legalEntityId}", async (string legalEntityId, ControlStore store, CancellationToken cancellationToken) =>
{
    var route = await store.FindLegalEntityAsync(legalEntityId, cancellationToken).ConfigureAwait(false);
    return route is null ? Results.NotFound() : Results.Ok(route);
});
app.MapPost("/zones", async (RegisterZone zone, ControlStore store, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(zone.ZoneId) || string.IsNullOrWhiteSpace(zone.DisplayName) || string.IsNullOrWhiteSpace(zone.ApiBaseUrl))
        return Results.BadRequest();
    if (zone.Status is not ("provisioning" or "active"))
        return Results.BadRequest();

    var created = await store.RegisterZoneAsync(zone, cancellationToken).ConfigureAwait(false);
    return created ? Results.Created($"/zones/{zone.ZoneId}", zone) : Results.Conflict();
});
app.MapPost("/routing/legal-entities", async (RegisterLegalEntity entity, ControlStore store, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(entity.LegalEntityId) || string.IsNullOrWhiteSpace(entity.ZoneId) || string.IsNullOrWhiteSpace(entity.DisplayName))
        return Results.BadRequest();

    var created = await store.RegisterLegalEntityAsync(entity, cancellationToken).ConfigureAwait(false);
    return created ? Results.Created($"/routing/legal-entities/{entity.LegalEntityId}", entity) : Results.Conflict();
});
app.Run();

public partial class Program;

internal sealed record RegistryKey(string Value);
