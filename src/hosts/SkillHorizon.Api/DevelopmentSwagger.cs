using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SkillHorizon.Api;

internal static class DevelopmentSwagger
{
    public static void AddDevelopmentSwagger(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
            return;

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "SkillHorizon API", Version = "v1" });
            options.CustomOperationIds(description => OperationId(description.HttpMethod, description.RelativePath));
            options.OperationFilter<ApiHeaderOperationFilter>();
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

    internal static string OperationId(string? method, string? path)
    {
        var value = (method ?? "get").ToLowerInvariant() + "_" + (path ?? "root");
        return value.Replace("/", "_", StringComparison.Ordinal)
            .Replace("{", "", StringComparison.Ordinal)
            .Replace("}", "", StringComparison.Ordinal)
            .Replace(":", "", StringComparison.Ordinal);
    }
}

internal sealed class ApiHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = (context.ApiDescription.RelativePath ?? "").TrimStart('/');
        if (ReadsWorkspace(path))
            HeaderParameters.Add(operation, SkillHorizon.Hosting.SkillHorizonContext.WorkspaceHeader, required: true, "Active workspace code read by this endpoint.");
        if (path.StartsWith("aggregates/", StringComparison.Ordinal))
            HeaderParameters.Add(operation, SkillHorizon.Hosting.SkillHorizonContext.RegistryKeyHeader, required: true, "Registry key read before this endpoint runs.");
        if (path == "internal/sittings/authorizations")
            HeaderParameters.Add(operation, "X-Sitting-Token", required: true, "Sitting token read by this endpoint.");
    }

    private static bool ReadsWorkspace(string path) => path is not (
        "health"
        or "access/workspaces"
        or "internal/results"
        or "internal/sittings/authorizations")
        && !path.StartsWith("aggregates/", StringComparison.Ordinal);
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
