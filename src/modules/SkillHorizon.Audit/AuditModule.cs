using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Audit;

public static class AuditModule
{
    public const string Schema = "audit";

    public static IServiceCollection AddAuditModule(this IServiceCollection services) => services;
}
