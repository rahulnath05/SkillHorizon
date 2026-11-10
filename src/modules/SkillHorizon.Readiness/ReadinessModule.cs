using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Readiness;

public static class ReadinessModule
{
    public const string Schema = "readiness";

    public static IServiceCollection AddReadinessModule(this IServiceCollection services)
    {
        services.AddSingleton<IPersonGap, PostgresPersonGap>();
        return services;
    }
}
