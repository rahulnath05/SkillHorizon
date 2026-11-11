using SkillHorizon.Access;
using SkillHorizon.Activation;
using SkillHorizon.Hosting;
using SkillHorizon.Readiness;
using SkillHorizon.Workforce;

namespace SkillHorizon.Api;

public static class LearningEndpoints
{
    public static WebApplication MapLearning(this WebApplication app)
    {
        app.MapPost("/commands/activation/budget", SetBudget);
        app.MapGet("/activation/centre", Centre);
        app.MapPost("/commands/waves/admit", Admit);
        app.MapPost("/commands/waves/rolling", Rolling);
        app.MapPost("/commands/sittings/authorize", Authorize);
        app.MapGet("/assessment", Summary);
        app.MapGet("/internal/sittings/authorizations", InternalGrant);
        app.MapPost("/internal/results", ApplyResult);
        app.MapGet("/employee/subject", EmployeeSubject);
        app.MapGet("/employee/skills", Skills);
        app.MapPost("/commands/paths/open", OpenPath);
        app.MapPost("/commands/validations/authorize", AuthorizeValidation);
        return app;
    }

    private static Task<IResult> SetBudget(HttpContext context, IGrantCheck grants, IWaveAdmission waves, Microsoft.Extensions.Options.IOptions<SkillHorizon.Hosting.SkillHorizonOptions> options, BudgetRequest? body, CancellationToken cancellationToken) =>
        LearningCommand(context, grants, cancellationToken, async () =>
        {
            await waves.SetBudgetAsync(options.Value.ZoneId, body?.SittingBudget ?? 0, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });

    private static async Task<IResult> Centre(HttpContext context, IGrantCheck grants, IWaveAdmission waves, Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.LearningAndDevelopment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(await waves.ReadCentreAsync(options.Value.ZoneId, cancellationToken).ConfigureAwait(false));
    }

    private static Task<IResult> Admit(HttpContext context, IGrantCheck grants, IWaveAdmission waves, Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options, AdmitRequest? body, CancellationToken cancellationToken) =>
        LearningCommand(context, grants, cancellationToken, async () =>
        {
            if (body is null)
                throw new InvalidOperationException("A wave needs a benchmark version and a population.");
            var wave = await waves.AdmitAsync(options.Value.ZoneId, body.BenchmarkVersionId, body.SubjectIds ?? [], Subject(context), Correlation(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(wave);
        });

    private static async Task<IResult> Rolling(
        HttpContext context,
        IGrantCheck grants,
        IWaveAdmission waves,
        IZoneDirectory directory,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options,
        RollingRequest? body,
        CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.LearningAndDevelopment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;

        var subjectId = body?.SubjectId?.Trim() ?? "";
        if (subjectId.Length == 0 || body is null || body.WaveId == Guid.Empty)
            return Results.Problem(title: "Wave refused", detail: "Rolling intake admits one person into an open wave.", statusCode: StatusCodes.Status400BadRequest);

        var person = await waves.FindPersonAsync(subjectId, cancellationToken).ConfigureAwait(false);
        if (person is null)
            return Results.Problem(title: "Wave refused", detail: "That person is not in this zone.", statusCode: StatusCodes.Status400BadRequest);

        ZoneRoute? route;
        try
        {
            route = await directory.FindByLegalEntityAsync(person.LegalEntityId, cancellationToken).ConfigureAwait(false);
        }
        catch (ZoneDirectoryUnavailableException)
        {
            return Results.Problem(title: "Zone directory unavailable", detail: "The legal-entity route could not be read.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (route is null
            || !string.Equals(route.ZoneId, options.Value.ZoneId, StringComparison.Ordinal)
            || !string.Equals(route.Status, "active", StringComparison.OrdinalIgnoreCase))
            return Results.Problem(title: "Wave refused", detail: "That legal entity is not mapped to this zone.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var wave = await waves.AdmitRollingAsync(options.Value.ZoneId, body.WaveId, subjectId, Subject(context), Correlation(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(wave);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Wave refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Authorize(HttpContext context, IGrantCheck grants, IWaveAdmission waves, AuthorizeRequest? body, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.Employee, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var issued = await waves.AuthorizeAsync(body?.WaveId ?? Guid.Empty, Subject(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(new
            {
                issued.Token,
                issued.Grant.AuthorizationId,
                issued.Grant.WaveId,
                issued.Grant.BenchmarkVersionId,
                issued.Grant.Skills,
                issued.Grant.Scale
            });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Not admitted", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Summary(HttpContext context, IGrantCheck grants, IWaveAdmission waves, IPersonGap gap, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.Employee, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var subject = Subject(context);
        var wave = await waves.CurrentWaveAsync(subject, cancellationToken).ConfigureAwait(false);
        var gaps = await gap.ReadAsync(subject, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { wave, gap = gaps });
    }

    private static async Task<IResult> InternalGrant(HttpContext context, IWaveAdmission waves, CancellationToken cancellationToken)
    {
        if (context.Items[LearningServiceGate.Item] is not true)
            return Results.Unauthorized();
        var token = context.Request.Headers["X-Sitting-Token"].ToString();
        var grant = await waves.ReadGrantAsync(token, cancellationToken).ConfigureAwait(false);
        return grant is null
            ? Results.Problem(title: "Sitting refused", detail: "That sitting token is not active.", statusCode: StatusCodes.Status403Forbidden)
            : Results.Ok(grant);
    }

    private static async Task<IResult> ApplyResult(HttpContext context, IPersonGap gap, ApplyResultRequest? body, CancellationToken cancellationToken)
    {
        if (context.Items[LearningServiceGate.Item] is not true)
            return Results.Unauthorized();
        try
        {
            if (body is null)
                throw new InvalidOperationException("A sealed result needs an authorization and measured skills.");
            var gaps = await gap.ApplyAsync(body.AuthorizationId, body.ResultId, body.Proficiencies ?? [], Correlation(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { gap = gaps });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Result refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> OpenPath(HttpContext context, IGrantCheck grants, IPersonGap gap, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.Employee, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var plan = await gap.PlanAsync(Subject(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(plan);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Path refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> AuthorizeValidation(HttpContext context, IGrantCheck grants, ITeamDirectory team, ValidationRequest? body, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.PeopleManager, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var link = await team.FindCurrentLinkAsync(Subject(context), body?.SubjectId ?? "", cancellationToken).ConfigureAwait(false);
        return link is null
            ? Results.Problem(title: "Validation refused", detail: "That person is not an active direct report.", statusCode: StatusCodes.Status403Forbidden)
            : Results.Ok(link);
    }

    private static async Task<IResult> EmployeeSubject(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.Employee, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(new { subjectId = Subject(context) });
    }

    private static async Task<IResult> Skills(HttpContext context, IGrantCheck grants, IPersonGap gap, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.Employee, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var other = OtherPerson(context);
        if (other is not null)
            return other;
        var skills = await gap.ReadSkillsAsync(Subject(context), cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { skills });
    }

    private static IResult? OtherPerson(HttpContext context)
    {
        var requested = context.Request.Query["subject"].ToString();
        if (string.IsNullOrWhiteSpace(requested) || string.Equals(requested, Subject(context), StringComparison.Ordinal))
            return null;
        return Results.Problem(
            title: "Record refused",
            detail: "Another person's record is not available in this workspace.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> LearningCommand(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken, Func<Task<IResult>> action)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.LearningAndDevelopment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Wave refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult?> RefuseUnless(HttpContext context, IGrantCheck grants, string workspace, CancellationToken cancellationToken)
    {
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (string.Equals(active, workspace, StringComparison.Ordinal)
            && await grants.HasGrantAsync(Subject(context), workspace, cancellationToken).ConfigureAwait(false))
            return null;
        return Results.Problem(title: "Grant required", detail: "This command is refused for the active workspace.", statusCode: StatusCodes.Status403Forbidden);
    }

    private static string Subject(HttpContext context) => context.User.FindFirst("sub")?.Value ?? "";

    private static string Correlation(HttpContext context) =>
        context.Items[SkillHorizonContext.CorrelationItem]?.ToString() is { Length: > 0 } value ? value : "learning";

    private sealed record BudgetRequest(int SittingBudget);

    private sealed record AdmitRequest(Guid BenchmarkVersionId, string[]? SubjectIds);

    private sealed record RollingRequest(Guid WaveId, string? SubjectId);

    private sealed record AuthorizeRequest(Guid WaveId);

    private sealed record ApplyResultRequest(Guid AuthorizationId, Guid ResultId, MeasuredSkill[]? Proficiencies);

    private sealed record ValidationRequest(string? SubjectId);
}
