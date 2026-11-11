using Npgsql;
using SkillHorizon.Access;
using SkillHorizon.Attestation;
using SkillHorizon.Hosting;
using SkillHorizon.Readiness;

namespace SkillHorizon.Api;

public static class PracticeEndpoints
{
    public static WebApplication MapPractice(this WebApplication app)
    {
        app.MapPost("/commands/practice-scope", GrantScope);
        app.MapPost("/commands/packs/submit", SubmitPack);
        app.MapPost("/commands/packs/{packId:guid}/reviews", MarkReviewed);
        app.MapPost("/commands/packs/{packId:guid}/decisions", DecideException);
        app.MapPost("/commands/packs/{packId:guid}/seal", SealPack);
        app.MapGet("/packs", ListPacks);
        app.MapGet("/practice/briefing", Briefing);
        app.MapGet("/practice/requests", Requests);
        app.MapPost("/practice/requests/{requestId:guid}/accept", AcceptRequest);
        app.MapPost("/commands/cohorts/limit", SetLimit);
        app.MapPost("/commands/cohorts", CreateCohort);
        app.MapGet("/practice/readiness", ReadSnapshot);
        return app;
    }

    private static Task<IResult> GrantScope(HttpContext context, IGrantCheck grants, IPracticeScope scope, ScopeRequest? body, CancellationToken cancellationToken) =>
        Learning(context, grants, cancellationToken, async () =>
        {
            var granted = await scope.GrantAsync(body?.SubjectId?.Trim() ?? "", body?.PracticeCode?.Trim() ?? "", cancellationToken).ConfigureAwait(false);
            if (!granted)
                throw new InvalidOperationException("That practice is not in the taxonomy.");
            return Results.NoContent();
        });

    private static Task<IResult> SubmitPack(HttpContext context, IGrantCheck grants, IAttestationPacks packs, PackRequest? body, CancellationToken cancellationToken) =>
        Learning(context, grants, cancellationToken, async () =>
        {
            var rows = (body?.Rows ?? []).Select(row => new PackRowWrite(row.JobProfileId, row.Classification?.Trim() ?? "", row.ExceptionNote)).ToArray();
            var pack = await packs.SubmitAsync(Subject(context), Correlation(context), body?.PracticeCode?.Trim() ?? "", rows, cancellationToken).ConfigureAwait(false);
            return Results.Ok(pack);
        });

    private static async Task<IResult> MarkReviewed(Guid packId, HttpContext context, IGrantCheck grants, IAttestationPacks packs, ReviewRequest? body, CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var pack = await packs.MarkReviewedAsync(Subject(context), Correlation(context), packId, body?.RowIds ?? [], cancellationToken).ConfigureAwait(false);
            return Results.Ok(pack);
        }
        catch (InvalidOperationException exception)
        {
            return Problem("Review refused", exception);
        }
    }

    private static async Task<IResult> DecideException(Guid packId, HttpContext context, IGrantCheck grants, IAttestationPacks packs, DecisionRequest? body, CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var pack = await packs.DecideAsync(Subject(context), Correlation(context), packId, body?.RowId ?? Guid.Empty, body?.Decision ?? "", cancellationToken).ConfigureAwait(false);
            return Results.Ok(pack);
        }
        catch (InvalidOperationException exception)
        {
            return Problem("Decision refused", exception);
        }
    }

    private static async Task<IResult> SealPack(Guid packId, HttpContext context, IGrantCheck grants, IAttestationPacks packs, CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            return Results.Ok(await packs.SealAsync(Subject(context), Correlation(context), packId, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return Problem("Seal refused", exception);
        }
    }

    private static async Task<IResult> Briefing(
        HttpContext context,
        IGrantCheck grants,
        IAttestationPacks packs,
        IPracticeScope scope,
        IPracticeProjection projection,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var practice = context.Request.Query["practice"].ToString();
        if (!await scope.HoldsAsync(Subject(context), practice, cancellationToken).ConfigureAwait(false))
            return Results.Problem(title: "Scope required", detail: "That practice is outside this lead's scope.", statusCode: StatusCodes.Status403Forbidden);
        var listed = await packs.ListAsync(practice, cancellationToken).ConfigureAwait(false);
        var open = listed.FirstOrDefault(pack => pack.Status == "submitted") ?? listed.FirstOrDefault();
        var requests = await new PracticeReviewStore(dataSource).ListAsync(practice, cancellationToken).ConfigureAwait(false);
        var snapshot = await projection.ReadAsync(practice, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { practiceCode = practice, pack = open, requests, snapshot });
    }

    private static async Task<IResult> Requests(HttpContext context, IGrantCheck grants, IPracticeScope scope, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var practice = context.Request.Query["practice"].ToString();
        if (!await scope.HoldsAsync(Subject(context), practice, cancellationToken).ConfigureAwait(false))
            return Results.Problem(title: "Scope required", detail: "That practice is outside this lead's scope.", statusCode: StatusCodes.Status403Forbidden);
        var requests = await new PracticeReviewStore(dataSource).ListAsync(practice, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { requests });
    }

    private static async Task<IResult> AcceptRequest(
        Guid requestId,
        HttpContext context,
        IGrantCheck grants,
        IPracticeScope scope,
        NpgsqlDataSource dataSource,
        AcceptRequestBody? body,
        CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var practice = body?.PracticeCode?.Trim() ?? "";
        if (!await scope.HoldsAsync(Subject(context), practice, cancellationToken).ConfigureAwait(false))
            return Results.Problem(title: "Scope required", detail: "That practice is outside this lead's scope.", statusCode: StatusCodes.Status403Forbidden);
        try
        {
            var request = await new PracticeReviewStore(dataSource).AcceptAsync(Subject(context), practice, requestId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(request);
        }
        catch (InvalidOperationException exception)
        {
            return Problem("Request refused", exception);
        }
    }

    private static async Task<IResult> ListPacks(HttpContext context, IGrantCheck grants, IAttestationPacks packs, IPracticeScope scope, CancellationToken cancellationToken)
    {
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (active == Workspaces.LearningAndDevelopment
            && await grants.HasGrantAsync(Subject(context), active, cancellationToken).ConfigureAwait(false))
            return Results.Ok(new { packs = await packs.ListAsync(null, cancellationToken).ConfigureAwait(false) });

        if (active == Workspaces.CompetencyLead
            && await grants.HasGrantAsync(Subject(context), active, cancellationToken).ConfigureAwait(false))
        {
            var practice = context.Request.Query["practice"].ToString();
            if (!await scope.HoldsAsync(Subject(context), practice, cancellationToken).ConfigureAwait(false))
                return Results.Problem(title: "Scope required", detail: "That practice is outside this lead's scope.", statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { packs = await packs.ListAsync(practice, cancellationToken).ConfigureAwait(false) });
        }

        return Results.Problem(title: "Grant required", detail: "This command is refused for the active workspace.", statusCode: StatusCodes.Status403Forbidden);
    }

    private static Task<IResult> SetLimit(HttpContext context, IGrantCheck grants, ICohorts cohorts, Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options, LimitRequest? body, CancellationToken cancellationToken) =>
        Learning(context, grants, cancellationToken, async () =>
        {
            await cohorts.SetMaximumAsync(options.Value.ZoneId, body?.Maximum ?? 0, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });

    private static async Task<IResult> CreateCohort(HttpContext context, IGrantCheck grants, ICohorts cohorts, Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options, CohortRequest? body, CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var cohort = await cohorts.CreateAsync(
                options.Value.ZoneId,
                Subject(context),
                Correlation(context),
                body?.PracticeCode?.Trim() ?? "",
                body?.CountryCode,
                body?.OrgUnit,
                body?.JobProfileId ?? Guid.Empty,
                body?.MemberIds ?? [],
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(cohort);
        }
        catch (InvalidOperationException exception)
        {
            return Problem("Cohort refused", exception);
        }
    }

    private static IResult Problem(string title, InvalidOperationException exception)
    {
        var status = exception.Message.Contains("scope", StringComparison.Ordinal)
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status400BadRequest;
        return Results.Problem(title: title, detail: exception.Message, statusCode: status);
    }

    private static async Task<IResult> ReadSnapshot(HttpContext context, IGrantCheck grants, IPracticeScope scope, IPracticeProjection projection, CancellationToken cancellationToken)
    {
        var refused = await Refuse(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var practice = context.Request.Query["practice"].ToString();
        if (!await scope.HoldsAsync(Subject(context), practice, cancellationToken).ConfigureAwait(false))
            return Results.Problem(title: "Scope required", detail: "That practice is outside this lead's scope.", statusCode: StatusCodes.Status403Forbidden);
        var snapshot = await projection.ReadAsync(practice, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { snapshot });
    }

    private static async Task<IResult> Learning(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken, Func<Task<IResult>> action)
    {
        var refused = await Refuse(context, grants, Workspaces.LearningAndDevelopment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Pack refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult?> Refuse(HttpContext context, IGrantCheck grants, string workspace, CancellationToken cancellationToken)
    {
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (string.Equals(active, workspace, StringComparison.Ordinal)
            && await grants.HasGrantAsync(Subject(context), workspace, cancellationToken).ConfigureAwait(false))
            return null;
        return Results.Problem(title: "Grant required", detail: "This command is refused for the active workspace.", statusCode: StatusCodes.Status403Forbidden);
    }

    private static string Subject(HttpContext context) => context.User.FindFirst("sub")?.Value ?? "";

    private static string Correlation(HttpContext context) =>
        context.Items[SkillHorizonContext.CorrelationItem]?.ToString() is { Length: > 0 } value ? value : "practice";

    private sealed record ScopeRequest(string? SubjectId, string? PracticeCode);

    private sealed record PackRowRequest(Guid JobProfileId, string? Classification, string? ExceptionNote);

    private sealed record PackRequest(string? PracticeCode, PackRowRequest[]? Rows);

    private sealed record LimitRequest(int Maximum);

    private sealed record CohortRequest(string? PracticeCode, string? CountryCode, string? OrgUnit, Guid JobProfileId, string[]? MemberIds);

    private sealed record ReviewRequest(Guid[]? RowIds);

    private sealed record DecisionRequest(Guid RowId, string? Decision);

    private sealed record AcceptRequestBody(string? PracticeCode);
}
