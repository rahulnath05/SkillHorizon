using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SkillHorizon.Worker;

internal static class DevelopmentSwagger
{
    public static void AddDevelopmentSwagger(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
            return;

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "SkillHorizon Worker", Version = "v1" });
            options.CustomOperationIds(description => OperationId(description.HttpMethod, description.RelativePath));
            options.DocumentFilter<HealthDocumentFilter>();
        });
    }

    public static void UseDevelopmentSwagger(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return;

        app.UseSwagger();
        app.UseSwaggerUI();
    }

    private static string OperationId(string? method, string? path)
    {
        var value = (method ?? "get").ToLowerInvariant() + "_" + (path ?? "root");
        return value.Replace("/", "_", StringComparison.Ordinal)
            .Replace("{", "", StringComparison.Ordinal)
            .Replace("}", "", StringComparison.Ordinal)
            .Replace(":", "", StringComparison.Ordinal);
    }
}

internal sealed class HealthDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (swaggerDoc.Paths.ContainsKey("/health") || swaggerDoc.Paths.ContainsKey("health"))
            return;

        var health = new OpenApiPathItem();
        health.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Summary = "Health",
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Description = "Healthy" },
                ["503"] = new OpenApiResponse { Description = "Unhealthy" }
            }
        });
        swaggerDoc.Paths.Add("/health", health);
    }
}
