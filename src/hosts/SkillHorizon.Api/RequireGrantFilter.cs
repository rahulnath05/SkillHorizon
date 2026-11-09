using SkillHorizon.Access;
using SkillHorizon.Hosting;

namespace SkillHorizon.Api;

public sealed class RequireGrantFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var subject = http.User.FindFirst("sub")?.Value;
        var workspace = http.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        var zoneId = http.Items[SkillHorizonContext.ZoneItem]?.ToString() ?? "";
        var correlationId = http.Items[SkillHorizonContext.CorrelationItem]?.ToString() ?? "";
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SkillHorizon.GrantCheck");
        var grants = http.RequestServices.GetRequiredService<IGrantCheck>();
        var allowed = !string.IsNullOrWhiteSpace(subject)
            && Workspaces.IsKnown(workspace)
            && await grants.HasGrantAsync(subject, workspace, http.RequestAborted).ConfigureAwait(false);

        if (!allowed)
        {
            logger.LogInformation(
                "Command refused. zone={Zone} correlation_id={CorrelationId} subject={SubjectId} workspace={Workspace}",
                zoneId,
                correlationId,
                subject ?? "",
                workspace);
            return Results.Problem(
                title: "Grant required",
                detail: "This command is refused until a grant exists for the active workspace.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }
}
