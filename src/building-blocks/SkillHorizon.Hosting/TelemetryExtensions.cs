using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SkillHorizon.Hosting;

public static class TelemetryExtensions
{
    public static WebApplicationBuilder AddSkillHorizonTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        var zoneId = builder.Configuration[$"{SkillHorizonOptions.SectionName}:ZoneId"] ?? "";
        var console = !string.Equals(
            builder.Configuration[$"{SkillHorizonOptions.SectionName}:EnableConsoleExporter"],
            "false",
            StringComparison.OrdinalIgnoreCase);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes([new KeyValuePair<string, object>("zone", zoneId)]))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                if (console)
                    tracing.AddConsoleExporter();
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (console)
                logging.AddConsoleExporter();
        });

        return builder;
    }
}
