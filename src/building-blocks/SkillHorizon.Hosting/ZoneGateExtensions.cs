using Microsoft.Extensions.Options;

namespace SkillHorizon.Hosting;

public static class ZoneGateExtensions
{
    public static WebApplication UseSkillHorizonZoneGate(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health") || context.Items[LearningServiceGate.Item] is true)
            {
                await next().ConfigureAwait(false);
                return;
            }

            var options = context.RequestServices.GetRequiredService<IOptions<SkillHorizonOptions>>().Value;
            var subject = context.User.FindFirst("sub")?.Value ?? "";
            var legalEntity = context.User.FindFirst("legal_entity")?.Value ?? "";
            var correlationId = context.Items[SkillHorizonContext.CorrelationItem]?.ToString() ?? "";
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SkillHorizon.ZoneGate");

            if (string.IsNullOrWhiteSpace(legalEntity))
            {
                Refuse(logger, options.ZoneId, correlationId, subject, legalEntity);
                await WriteUnmapped(context, options.ZoneId, correlationId).ConfigureAwait(false);
                return;
            }

            var directory = context.RequestServices.GetRequiredService<IZoneDirectory>();
            ZoneRoute? route;
            try
            {
                route = await directory.FindByLegalEntityAsync(legalEntity, context.RequestAborted).ConfigureAwait(false);
            }
            catch (ZoneDirectoryUnavailableException exception)
            {
                logger.LogWarning(
                    exception,
                    "Zone routing unavailable. zone={Zone} correlation_id={CorrelationId}",
                    options.ZoneId,
                    correlationId);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Zone routing unavailable",
                    detail = "The control registry could not be reached. This sign-in was not placed in another zone.",
                    status = StatusCodes.Status503ServiceUnavailable,
                    zone = options.ZoneId,
                    correlationId
                }).ConfigureAwait(false);
                return;
            }

            if (route is null || !string.Equals(route.Status, "active", StringComparison.Ordinal) ||
                !string.Equals(route.ZoneId, options.ZoneId, StringComparison.Ordinal))
            {
                Refuse(logger, options.ZoneId, correlationId, subject, legalEntity);
                await WriteUnmapped(context, options.ZoneId, correlationId).ConfigureAwait(false);
                return;
            }

            await next().ConfigureAwait(false);
        });

        return app;
    }

    private static void Refuse(ILogger logger, string zoneId, string correlationId, string subject, string legalEntity)
    {
        logger.LogInformation(
            "Unmapped person refused. zone={Zone} correlation_id={CorrelationId} subject={SubjectId} legal_entity={LegalEntityId}",
            zoneId,
            correlationId,
            subject,
            legalEntity);
    }

    private static Task WriteUnmapped(HttpContext context, string zoneId, string correlationId)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new
        {
            title = "Unmapped person",
            detail = "No active zone is registered for this legal entity. SkillHorizon will not place an unmapped person into another zone.",
            status = StatusCodes.Status403Forbidden,
            zone = zoneId,
            correlationId
        });
    }
}
