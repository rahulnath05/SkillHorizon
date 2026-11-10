using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.JobArchitecture;

public static class JobArchitectureModule
{
    public const string Schema = "job_architecture";

    public static IServiceCollection AddJobArchitectureModule(this IServiceCollection services)
    {
        services.AddSingleton<IJobArchitecture, PostgresJobArchitecture>();
        return services;
    }
}
