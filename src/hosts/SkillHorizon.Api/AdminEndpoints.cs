using SkillHorizon.Access;
using SkillHorizon.Audit;
using SkillHorizon.Hosting;

namespace SkillHorizon.Api;

public static class AdminEndpoints
{
    public static WebApplication MapAdmin(this WebApplication app)
    {
        app.MapGet("/admin/grants", Grants);
        app.MapPost("/commands/admin/grants", Grant);
        app.MapPost("/commands/admin/grants/revoke", Revoke);
        app.MapGet("/admin/security", Security);
        app.MapGet("/admin/models", Models);
        app.MapGet("/admin/audit", Audit);
        app.MapGet("/admin/malpractice", Cases);
        app.MapPost("/commands/admin/malpractice", OpenCase);
        app.MapGet("/admin/integrations", Integrations);
        app.MapGet("/admin/exports", Exports);
        app.MapPost("/commands/admin/exports/personal", ExportPersonal);
        return app;
    }

    private static async Task<IResult> Grants(HttpContext context, IGrantCheck grants, IWorkspaceAdministration administration, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var rows = await administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { grants = rows });
    }

    private static async Task<IResult> Grant(HttpContext context, IGrantCheck grants, IWorkspaceAdministration administration, GrantChange? body, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var row = await administration.GrantAsync(Subject(context), body?.SubjectId ?? "", body?.Workspace ?? "", Correlation(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(row);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Grant refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Revoke(HttpContext context, IGrantCheck grants, IWorkspaceAdministration administration, GrantChange? body, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            await administration.RevokeAsync(Subject(context), body?.SubjectId ?? "", body?.Workspace ?? "", Correlation(context), cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Grant refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Security(HttpContext context, IGrantCheck grants, IZoneDirectory directory, Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var zoneId = options.Value.ZoneId;
            var residency = await directory.ReadResidencyAsync(zoneId, cancellationToken).ConfigureAwait(false);
            if (residency is null)
                return Results.Problem(title: "Zone missing", detail: "This zone is not in the control registry.", statusCode: StatusCodes.Status404NotFound);
            var connectors = await directory.ListConnectorsAsync(zoneId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { residency, connectors });
        }
        catch (ZoneDirectoryUnavailableException)
        {
            return Results.Problem(title: "Zone directory unavailable", detail: "Residency and connector status could not be read.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> Models(HttpContext context, IGrantCheck grants, IPlatformRegister register, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var models = await register.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { models });
    }

    private static async Task<IResult> Audit(HttpContext context, IGrantCheck grants, IPlatformRegister register, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var records = await register.ListAuditAsync(Subject(context), Correlation(context), cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { records });
    }

    private static async Task<IResult> Cases(HttpContext context, IGrantCheck grants, IPlatformRegister register, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var cases = await register.ListCasesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { cases });
    }

    private static async Task<IResult> OpenCase(HttpContext context, IGrantCheck grants, IPlatformRegister register, CaseRequest? body, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var opened = await register.OpenCaseAsync(Subject(context), body?.SittingId ?? Guid.Empty, body?.Status ?? "", cancellationToken).ConfigureAwait(false);
            return Results.Ok(opened);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Case refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Integrations(HttpContext context, IGrantCheck grants, IZoneDirectory directory, Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var connectors = await directory.ListConnectorsAsync(options.Value.ZoneId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { connectors });
        }
        catch (ZoneDirectoryUnavailableException)
        {
            return Results.Problem(title: "Zone directory unavailable", detail: "The connector registry could not be read.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> Exports(HttpContext context, IGrantCheck grants, IPlatformRegister register, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(new { types = register.ListExportTypes() });
    }

    private static async Task<IResult> ExportPersonal(HttpContext context, IGrantCheck grants, IPlatformRegister register, ExportRequest? body, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var export = await register.ExportPersonalMetadataAsync(Subject(context), body?.SubjectId ?? "", Correlation(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(export);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Export refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult?> RefuseUnless(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken)
    {
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (string.Equals(active, Workspaces.PlatformAdmin, StringComparison.Ordinal)
            && await grants.HasGrantAsync(Subject(context), Workspaces.PlatformAdmin, cancellationToken).ConfigureAwait(false))
            return null;
        return Results.Problem(title: "Grant required", detail: "This command is refused for the active workspace.", statusCode: StatusCodes.Status403Forbidden);
    }

    private static string Subject(HttpContext context) => context.User.FindFirst("sub")?.Value ?? "";

    private static string Correlation(HttpContext context) =>
        context.Items[SkillHorizonContext.CorrelationItem]?.ToString() is { Length: > 0 } value ? value : "admin";

    private sealed record GrantChange(string? SubjectId, string? Workspace);

    private sealed record CaseRequest(Guid SittingId, string? Status);

    private sealed record ExportRequest(string? SubjectId);
}
