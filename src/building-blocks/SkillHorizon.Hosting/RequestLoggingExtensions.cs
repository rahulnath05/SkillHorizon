using Microsoft.Extensions.Options;

namespace SkillHorizon.Hosting;

public static class RequestLoggingExtensions
{
    public static WebApplication UseSkillHorizonRequestLogging(this WebApplication app)
    {
        var zoneId = app.Services.GetRequiredService<IOptions<SkillHorizonOptions>>().Value.ZoneId;
        app.Use(async (context, next) =>
        {
            var incoming = context.Request.Headers[SkillHorizonContext.CorrelationHeader].ToString();
            var correlationId = SkillHorizonContext.IsValidCorrelationId(incoming)
                ? incoming
                : Guid.NewGuid().ToString("N");

            context.Response.Headers[SkillHorizonContext.CorrelationHeader] = correlationId;
            context.Items[SkillHorizonContext.CorrelationItem] = correlationId;
            context.Items[SkillHorizonContext.ZoneItem] = zoneId;
            System.Diagnostics.Activity.Current?.SetTag("zone", zoneId);
            System.Diagnostics.Activity.Current?.SetTag("correlation_id", correlationId);

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SkillHorizon.Request");
            using (logger.BeginScope(new Dictionary<string, object>
            {
                ["zone"] = zoneId,
                ["correlation_id"] = correlationId
            }))
            {
                try
                {
                    await next().ConfigureAwait(false);
                }
                finally
                {
                    logger.LogInformation(
                        "Request completed. zone={Zone} correlation_id={CorrelationId} method={Method} path={Path} status={StatusCode}",
                        zoneId,
                        correlationId,
                        context.Request.Method,
                        context.Request.Path.Value,
                        context.Response.StatusCode);
                }
            }
        });

        return app;
    }
}
