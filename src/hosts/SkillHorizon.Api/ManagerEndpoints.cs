using SkillHorizon.Access;
using SkillHorizon.Hosting;
using SkillHorizon.Workforce;

namespace SkillHorizon.Api;

public static class ManagerEndpoints
{
    public static WebApplication MapManager(this WebApplication app)
    {
        app.MapGet("/workforce/team/snapshot", Snapshot);
        app.MapGet("/workforce/team/reviews", Reviews);
        app.MapPost("/workforce/team/profile-confirmations", ConfirmProfile);
        app.MapPost("/workforce/team/assessment-reviews", ReviewAssessment);
        app.MapPost("/workforce/team/corrections", FileCorrection);
        app.MapGet("/workforce/team/skills", Skills);
        app.MapGet("/workforce/team/commitments", Commitments);
        app.MapPost("/workforce/team/commitments", CreateCommitment);
        return app;
    }

    private static async Task<IResult> Snapshot(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var snapshot = await workspace.ReadSnapshotAsync(ManagerId(context), cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { snapshot });
    }

    private static async Task<IResult> Reviews(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(await workspace.ReadReviewsAsync(ManagerId(context), cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ConfirmProfile(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, ReportBody? body, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            await workspace.ConfirmProfileAsync(ManagerId(context), body?.SubjectId ?? "", cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(title: "Profile refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Profile refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ReviewAssessment(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, ReviewBody? body, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            await workspace.ReviewAssessmentAsync(ManagerId(context), body?.SubjectId ?? "", body?.Note ?? "", cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(title: "Review refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Review refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> FileCorrection(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, CorrectionBody? body, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var requestId = await workspace.FileCorrectionAsync(ManagerId(context), body?.SubjectId ?? "", body?.Note ?? "", cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { requestId });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(title: "Correction refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Correction refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Skills(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var people = await workspace.ReadSkillsAsync(ManagerId(context), cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { people });
    }

    private static async Task<IResult> Commitments(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var commitments = await workspace.ListCommitmentsAsync(ManagerId(context), cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { commitments });
    }

    private static async Task<IResult> CreateCommitment(HttpContext context, IGrantCheck grants, ITeamWorkspace workspace, CommitmentBody? body, CancellationToken cancellationToken)
    {
        var refused = await Manager(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            if (!DateOnly.TryParse(body?.DueOn, out var dueOn))
                throw new InvalidOperationException("A commitment needs a due date.");
            var commitment = await workspace.CreateCommitmentAsync(ManagerId(context), body?.SubjectId ?? "", body?.Title ?? "", dueOn, cancellationToken).ConfigureAwait(false);
            return Results.Ok(commitment);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(title: "Commitment refused", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Commitment refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult?> Manager(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken)
    {
        var workspace = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (!string.Equals(workspace, Workspaces.PeopleManager, StringComparison.Ordinal))
        {
            return Results.Problem(
                title: "Workspace required",
                detail: "This page is read from the people manager workspace.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var subject = context.User.FindFirst("sub")?.Value ?? "";
        if (!await grants.HasGrantAsync(subject, Workspaces.PeopleManager, cancellationToken).ConfigureAwait(false))
        {
            return Results.Problem(
                title: "Grant required",
                detail: "This page is refused until the people manager grant is held.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private static string ManagerId(HttpContext context)
    {
        var employeeId = context.User.FindFirst("employee_id")?.Value;
        return string.IsNullOrWhiteSpace(employeeId) ? context.User.FindFirst("sub")?.Value ?? "" : employeeId;
    }

    private sealed record ReportBody(string? SubjectId);

    private sealed record ReviewBody(string? SubjectId, string? Note);

    private sealed record CorrectionBody(string? SubjectId, string? Note);

    private sealed record CommitmentBody(string? SubjectId, string? Title, string? DueOn);
}
