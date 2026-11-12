using System.Net.Http.Json;
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
builder.Services.AddHttpClient("zones", client => client.Timeout = TimeSpan.FromSeconds(5));
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
app.MapPost("/zones/{zoneId}/reference", async (string zoneId, FlagRequest? body, ControlStore store, CancellationToken cancellationToken) =>
{
    if (body is null || !await ZoneExists(store, zoneId, cancellationToken).ConfigureAwait(false))
        return body is null ? Results.BadRequest() : Results.NotFound();

    await store.SetReferenceSubscriptionAsync(zoneId, body.Enabled, cancellationToken).ConfigureAwait(false);
    return Results.NoContent();
});
app.MapPost("/zones/{zoneId}/hr", async (string zoneId, FlagRequest? body, ControlStore store, CancellationToken cancellationToken) =>
{
    if (body is null || !await ZoneExists(store, zoneId, cancellationToken).ConfigureAwait(false))
        return body is null ? Results.BadRequest() : Results.NotFound();

    await store.SetHrFeedAsync(zoneId, body.Enabled, cancellationToken).ConfigureAwait(false);
    return Results.NoContent();
});
app.MapGet("/zones/{zoneId}/hr", async (string zoneId, ControlStore store, CancellationToken cancellationToken) =>
{
    if (!await ZoneExists(store, zoneId, cancellationToken).ConfigureAwait(false))
        return Results.NotFound();

    var enabled = await store.HrFeedEnabledAsync(zoneId, cancellationToken).ConfigureAwait(false);
    return Results.Ok(new { zoneId, enabled });
});
app.MapGet("/reference/subscribers", async (ControlStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.ListReferenceSubscribersAsync(cancellationToken).ConfigureAwait(false)));
app.MapGet("/zones/{zoneId}/residency", async (string zoneId, ControlStore store, CancellationToken cancellationToken) =>
{
    var residency = await store.ReadResidencyAsync(zoneId, cancellationToken).ConfigureAwait(false);
    return residency is null ? Results.NotFound() : Results.Ok(residency);
});
app.MapGet("/zones/{zoneId}/connectors", async (string zoneId, ControlStore store, CancellationToken cancellationToken) =>
{
    var connectors = await store.ListConnectorsAsync(zoneId, cancellationToken).ConfigureAwait(false);
    return connectors is null ? Results.NotFound() : Results.Ok(connectors);
});
app.MapPost("/zones/{zoneId}/activate", async (string zoneId, ControlStore store, CancellationToken cancellationToken) =>
{
    if (!await ZoneExists(store, zoneId, cancellationToken).ConfigureAwait(false))
        return Results.NotFound();

    var activated = await store.ActivateAsync(zoneId, cancellationToken).ConfigureAwait(false);
    return activated
        ? Results.NoContent()
        : Results.Conflict(new { title = "Zone is not ready", detail = "Subscribe to reference data and turn on the HR feed before the zone becomes active." });
});
app.MapGet("/aggregates/practice", async (string? practice, ControlStore store, IHttpClientFactory http, RegistryKey registryKey, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(practice))
        return Results.BadRequest();

    var zones = await store.ListZonesAsync(cancellationToken).ConfigureAwait(false);
    var active = zones.Where(zone => zone.Status == "active").ToArray();
    var client = http.CreateClient("zones");
    var parts = new List<ZonePracticeCount>();
    foreach (var zone in active)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, zone.ApiBaseUrl.TrimEnd('/') + "/aggregates/practice?practice=" + Uri.EscapeDataString(practice));
        request.Headers.TryAddWithoutValidation(SkillHorizon.Hosting.SkillHorizonContext.RegistryKeyHeader, registryKey.Value);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return Results.Json(new { title = "A zone aggregate could not be read", zone = zone.ZoneId }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var part = await response.Content.ReadFromJsonAsync<ZonePracticeCount>(cancellationToken).ConfigureAwait(false);
        if (part is null || !string.Equals(part.ZoneId, zone.ZoneId, StringComparison.Ordinal))
            return Results.Json(new { title = "A zone aggregate could not be read", zone = zone.ZoneId }, statusCode: StatusCodes.Status503ServiceUnavailable);

        parts.Add(part);
    }

    return Results.Ok(PracticeAggregate.Sum(practice, parts));
});
app.Run();

static async Task<bool> ZoneExists(ControlStore store, string zoneId, CancellationToken cancellationToken)
{
    var zones = await store.ListZonesAsync(cancellationToken).ConfigureAwait(false);
    return zones.Any(zone => string.Equals(zone.ZoneId, zoneId, StringComparison.Ordinal));
}

internal sealed record FlagRequest(bool Enabled);

public sealed record ZonePracticeCount(
    string ZoneId,
    string PracticeCode,
    int MemberCount,
    int MeasuredCount,
    int GapCount,
    int CohortCount,
    DateTimeOffset? AsOf);

public sealed record PracticeAggregateResult(
    string PracticeCode,
    int MemberCount,
    int MeasuredCount,
    int GapCount,
    int CohortCount,
    DateTimeOffset? AsOf,
    IReadOnlyList<ZonePracticeCount> Zones);

public static class PracticeAggregate
{
    public static PracticeAggregateResult Sum(string practiceCode, IReadOnlyList<ZonePracticeCount> zones)
    {
        DateTimeOffset? asOf = null;
        var members = 0;
        var measured = 0;
        var gaps = 0;
        var cohorts = 0;
        foreach (var zone in zones)
        {
            members += zone.MemberCount;
            measured += zone.MeasuredCount;
            gaps += zone.GapCount;
            cohorts += zone.CohortCount;
            if (zone.AsOf is DateTimeOffset at && (asOf is null || at < asOf))
                asOf = at;
        }

        return new PracticeAggregateResult(practiceCode, members, measured, gaps, cohorts, asOf, zones);
    }
}

public partial class Program;

internal sealed record RegistryKey(string Value);
