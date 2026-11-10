using SkillHorizon.Access;
using SkillHorizon.Hosting;
using SkillHorizon.Workforce;

namespace SkillHorizon.Api;

public static class WorkforceEndpoints
{
    public static WebApplication MapWorkforce(this WebApplication app)
    {
        app.MapGet("/workforce/team", Team);
        return app;
    }

    private static async Task<IResult> Team(
        HttpContext context,
        ITeamDirectory directory,
        IGrantCheck grants,
        CancellationToken cancellationToken)
    {
        var workspace = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (!string.Equals(workspace, Workspaces.PeopleManager, StringComparison.Ordinal))
        {
            return Results.Problem(
                title: "Workspace required",
                detail: "Direct reports are read from the people manager workspace.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var subject = context.User.FindFirst("sub")?.Value ?? "";
        if (!await grants.HasGrantAsync(subject, Workspaces.PeopleManager, cancellationToken).ConfigureAwait(false))
        {
            return Results.Problem(
                title: "Grant required",
                detail: "This directory is refused until the people manager grant is held.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var employeeId = context.User.FindFirst("employee_id")?.Value;
        if (string.IsNullOrWhiteSpace(employeeId))
            employeeId = subject;

        var people = await directory.ListDirectReportsAsync(employeeId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { people });
    }
}
