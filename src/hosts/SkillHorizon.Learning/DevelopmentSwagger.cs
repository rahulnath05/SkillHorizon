using Microsoft.OpenApi;
using SkillHorizon.Hosting;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SkillHorizon.Learning;

internal static class DevelopmentSwagger
{
    public static void AddDevelopmentSwagger(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
            return;

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "SkillHorizon Learning", Version = "v1" });
            options.CustomOperationIds(description => OperationId(description.HttpMethod, description.RelativePath));
            options.OperationFilter<LearningHeaderOperationFilter>();
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

internal sealed class LearningHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = (context.ApiDescription.RelativePath ?? "").TrimStart('/');
        if (path == "health")
            return;

        if (IsSitting(path))
            HeaderParameters.Add(operation, "X-Sitting-Token", required: true, "Sitting token read by this endpoint.");
        else
            HeaderParameters.Add(operation, SkillHorizonContext.WorkspaceHeader, required: true, "Active workspace code read by this endpoint.");

        if (ReadsCorrelation(path))
            HeaderParameters.Add(operation, SkillHorizonContext.CorrelationHeader, required: false, "Correlation id read by this endpoint.");
    }

    private static bool IsSitting(string path) =>
        path == "sittings" || path.StartsWith("sittings/", StringComparison.Ordinal);

    private static bool ReadsCorrelation(string path) =>
        path != "sittings" && !path.EndsWith("/responses", StringComparison.Ordinal);
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

internal static class HeaderParameters
{
    public static void Add(OpenApiOperation operation, string name, bool required, string description)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter => parameter.Name == name && parameter.In == ParameterLocation.Header))
            return;

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
    }
}
