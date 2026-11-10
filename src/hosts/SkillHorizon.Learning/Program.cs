using Npgsql;
using SkillHorizon.Database;
using SkillHorizon.Hosting;
using SkillHorizon.Learning;

var builder = WebApplication.CreateBuilder(args);
var options = builder.BindSkillHorizonOptions();
if (string.IsNullOrWhiteSpace(options.AssessmentConnectionString) || string.IsNullOrWhiteSpace(options.ProgressConnectionString))
    throw new InvalidOperationException("SkillHorizon assessment and progress connection strings are required.");
if (string.IsNullOrWhiteSpace(options.ApiUrl))
    throw new InvalidOperationException("SkillHorizon:ApiUrl is required.");

builder.AddSkillHorizonTelemetry("skillhorizon-learning");
builder.Services.AddSkillHorizonSecrets();
builder.Services.AddSingleton(NpgsqlDataSource.Create(options.AssessmentConnectionString));
builder.Services.AddSingleton(_ => new ProgressData(NpgsqlDataSource.Create(options.ProgressConnectionString)));
builder.Services.AddHttpClient<CoreCaller>((_, client) =>
{
    client.BaseAddress = new Uri(options.ApiUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IExternalCatalog, UnconnectedCatalog>();
builder.Services.AddSingleton(serviceProvider =>
{
    var progress = new ProgressSteps(serviceProvider.GetRequiredService<ProgressData>().Source, serviceProvider.GetRequiredService<CoreCaller>().Client);
    var key = serviceProvider.GetRequiredService<ISecretStore>()
        .GetRequiredAsync(options.LearningServiceKeySecretName, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    return new AssessmentSession(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
        serviceProvider.GetRequiredService<CoreCaller>().Client,
        progress,
        key);
});
builder.Services.AddSingleton(serviceProvider =>
    new ProgressSteps(serviceProvider.GetRequiredService<ProgressData>().Source, serviceProvider.GetRequiredService<CoreCaller>().Client));
builder.Services.AddSingleton<ItemFixture>();
builder.Services.AddHealthChecks()
    .AddNpgSql(options.AssessmentConnectionString, name: "assessment-postgres")
    .AddNpgSql(options.ProgressConnectionString, name: "progress-postgres");

var app = builder.Build();
if (options.MigrateOnStartup)
{
    SchemaMigrator.ApplyAssessment(options.AssessmentConnectionString);
    SchemaMigrator.ApplyProgress(options.ProgressConnectionString);
}

app.UseSkillHorizonRequestLogging();
app.MapHealthChecks("/health");
app.MapPost("/sittings", async (HttpContext context, AssessmentSession sessions, CancellationToken cancellationToken) =>
{
    try
    {
        var token = context.Request.Headers["X-Sitting-Token"].ToString();
        return Results.Ok(await sessions.OpenAsync(token, cancellationToken).ConfigureAwait(false));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Sitting refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapPost("/sittings/{sittingId:guid}/responses", async (Guid sittingId, HttpContext context, AssessmentSession sessions, AnswerBody? body, CancellationToken cancellationToken) =>
{
    try
    {
        var token = context.Request.Headers["X-Sitting-Token"].ToString();
        return Results.Ok(await sessions.AnswerAsync(sittingId, token, body?.ItemId ?? Guid.Empty, body?.Rank ?? 0, cancellationToken).ConfigureAwait(false));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Response refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapPost("/sittings/{sittingId:guid}/seal", async (Guid sittingId, HttpContext context, AssessmentSession sessions, CancellationToken cancellationToken) =>
{
    try
    {
        var token = context.Request.Headers["X-Sitting-Token"].ToString();
        var correlation = context.Request.Headers[SkillHorizonContext.CorrelationHeader].ToString();
        return Results.Ok(await sessions.SealAsync(sittingId, token, correlation, cancellationToken).ConfigureAwait(false));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Result refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapPost("/progress/steps", async (HttpContext context, ProgressSteps steps, ExternalStep? body, CancellationToken cancellationToken) =>
{
    if (!string.Equals(body?.Provider, "external", StringComparison.Ordinal))
        return Results.Problem(title: "Provider refused", detail: "The external provider is accepted and not connected. SkillHorizon assessment steps are sealed from a sitting.", statusCode: StatusCodes.Status400BadRequest);
    try
    {
        var bearer = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var token = bearer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? bearer[prefix.Length..].Trim() : "";
        await steps.AssignExternalAsync(
            token,
            context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString(),
            context.Request.Headers[SkillHorizonContext.CorrelationHeader].ToString(),
            body?.Title ?? "",
            cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Step refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});

if (options.LoadLearningFixture)
    app.Services.GetRequiredService<ItemFixture>().LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

app.Run();

public sealed record ProgressData(NpgsqlDataSource Source);

public sealed class CoreCaller
{
    public CoreCaller(HttpClient client)
    {
        Client = client;
    }

    public HttpClient Client { get; }
}

public sealed record AnswerBody(Guid ItemId, int Rank);

public sealed record ExternalStep(string Provider, string Title);

public partial class Program;
