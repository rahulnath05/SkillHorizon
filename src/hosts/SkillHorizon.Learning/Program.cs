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
if (string.IsNullOrWhiteSpace(options.EvidenceRoot))
    throw new InvalidOperationException("SkillHorizon:EvidenceRoot is required.");

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
builder.Services.AddSingleton<IEvidenceFiles>(_ => new FileEvidenceStore(options.EvidenceRoot));
builder.Services.AddSingleton(serviceProvider => new DevelopmentPaths(
    serviceProvider.GetRequiredService<ProgressData>().Source,
    serviceProvider.GetRequiredService<CoreCaller>().Client,
    serviceProvider.GetRequiredService<IEvidenceFiles>()));
builder.Services.AddSingleton(serviceProvider => new EmployeeRecord(
    serviceProvider.GetRequiredService<NpgsqlDataSource>(),
    serviceProvider.GetRequiredService<ProgressData>().Source,
    serviceProvider.GetRequiredService<CoreCaller>().Client,
    serviceProvider.GetRequiredService<IEvidenceFiles>()));
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

app.MapPost("/paths", async (HttpContext context, DevelopmentPaths paths, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await paths.OpenAsync(Incoming.Bearer(context), Incoming.Workspace(context), Incoming.Correlation(context), cancellationToken).ConfigureAwait(false));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Path refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapGet("/employee/activity", async (HttpContext context, EmployeeRecord record, CancellationToken cancellationToken) =>
{
    try
    {
        var activity = await record.ReadActivityAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            context.Request.Query["subject"].ToString(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(activity);
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Problem(title: "Record refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Record refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
});
app.MapGet("/documents", async (HttpContext context, EmployeeRecord record, CancellationToken cancellationToken) =>
{
    try
    {
        var documents = await record.ListDocumentsAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            context.Request.Query["subject"].ToString(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { documents });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Problem(title: "Record refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Record refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
});
app.MapGet("/documents/{evidenceId:guid}", async (Guid evidenceId, HttpContext context, EmployeeRecord record, CancellationToken cancellationToken) =>
{
    try
    {
        var file = await record.OpenDocumentAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            evidenceId,
            cancellationToken).ConfigureAwait(false);
        return Results.File(file.Content, file.ContentType, file.FileName);
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Problem(title: "Record refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Document refused", detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
    }
});
app.MapGet("/paths/current", async (HttpContext context, DevelopmentPaths paths, CancellationToken cancellationToken) =>
{
    try
    {
        var path = await paths.ReadOwnAsync(Incoming.Bearer(context), Incoming.Workspace(context), Incoming.Correlation(context), cancellationToken).ConfigureAwait(false);
        return path is null ? Results.NotFound() : Results.Ok(path);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Path refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapGet("/tasks", async (HttpContext context, DevelopmentPaths paths, CancellationToken cancellationToken) =>
{
    try
    {
        var tasks = await paths.ListTasksAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            context.Request.Query["subject"].ToString(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { tasks });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Problem(title: "Task refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Task refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
});
app.MapGet("/tasks/{stepId:guid}", async (Guid stepId, HttpContext context, DevelopmentPaths paths, CancellationToken cancellationToken) =>
{
    try
    {
        var task = await paths.ReadTaskAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            stepId,
            context.Request.Query["subject"].ToString(),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(task);
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Problem(title: "Task refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Task refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
});
app.MapPost("/tasks/{stepId:guid}/close", async (Guid stepId, HttpContext context, DevelopmentPaths paths, EvidenceBody? body, CancellationToken cancellationToken) =>
{
    try
    {
        var content = Convert.FromBase64String(body?.ContentBase64 ?? "");
        var task = await paths.CloseTaskAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            stepId,
            context.Request.Query["subject"].ToString(),
            body?.FileName ?? "",
            content,
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(task);
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Problem(title: "Task refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (Exception exception) when (exception is InvalidOperationException or FormatException)
    {
        return Results.Problem(title: "Task refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapPost("/paths/evidence", async (HttpContext context, DevelopmentPaths paths, EvidenceBody? body, CancellationToken cancellationToken) =>
{
    try
    {
        var content = Convert.FromBase64String(body?.ContentBase64 ?? "");
        return Results.Ok(await paths.AddEvidenceAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            body?.StepId ?? Guid.Empty,
            body?.FileName ?? "",
            content,
            cancellationToken).ConfigureAwait(false));
    }
    catch (Exception exception) when (exception is InvalidOperationException or FormatException)
    {
        _ = exception;
        return Results.Problem(title: "Evidence refused", detail: "That evidence was not stored.", statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapPost("/validations", async (HttpContext context, DevelopmentPaths paths, ValidationBody? body, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await paths.ValidateAsync(
            Incoming.Bearer(context),
            Incoming.Workspace(context),
            Incoming.Correlation(context),
            body?.SubjectId ?? "",
            body?.StepId ?? Guid.Empty,
            cancellationToken).ConfigureAwait(false));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Validation refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapPost("/progress/steps/{stepId:guid}/completions", async (Guid stepId, HttpContext context, DevelopmentPaths paths, CancellationToken cancellationToken) =>
{
    try
    {
        await paths.CompleteExternalAsync(Incoming.Bearer(context), Incoming.Workspace(context), Incoming.Correlation(context), stepId, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Completion refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
app.MapGet("/team/paths", async (HttpContext context, DevelopmentPaths paths, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(new { people = await paths.TeamAsync(Incoming.Bearer(context), Incoming.Workspace(context), Incoming.Correlation(context), cancellationToken).ConfigureAwait(false) });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Path refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
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

public sealed record EvidenceBody(Guid StepId, string? FileName, string? ContentBase64);

public sealed record ValidationBody(string? SubjectId, Guid StepId);

static class Incoming
{
    public static string Bearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : "";
    }

    public static string Workspace(HttpContext context) => context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();

    public static string Correlation(HttpContext context) => context.Request.Headers[SkillHorizonContext.CorrelationHeader].ToString();
}

public partial class Program;
