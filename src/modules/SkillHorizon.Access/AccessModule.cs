using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Access;

public static class AccessModule
{
    public const string Schema = "access";

    public static IServiceCollection AddAccessModule(this IServiceCollection services)
    {
        services.AddSingleton<IGrantCheck, PostgresGrantCheck>();
        services.AddSingleton<IWorkspaceAdministration, PostgresWorkspaceAdministration>();
        return services;
    }
}
