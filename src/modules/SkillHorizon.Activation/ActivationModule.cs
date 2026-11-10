using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Activation;

public static class ActivationModule
{
    public const string Schema = "activation";

    public static IServiceCollection AddActivationModule(this IServiceCollection services)
    {
        services.AddSingleton<IWaveAdmission, PostgresWaveAdmission>();
        return services;
    }
}
