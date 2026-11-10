using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Workforce;

public static class WorkforceModule
{
    public const string Schema = "workforce";

    public static IServiceCollection AddWorkforceModule(this IServiceCollection services)
    {
        services.AddSingleton<IHrFeed, FixtureHrFeed>();
        services.AddSingleton<IWorkforceSync, PostgresWorkforceSync>();
        services.AddSingleton<ITeamDirectory, PostgresTeamDirectory>();
        return services;
    }
}
