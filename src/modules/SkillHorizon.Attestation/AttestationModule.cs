using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Attestation;

public static class AttestationModule
{
    public const string Schema = "attestation";

    public static IServiceCollection AddAttestationModule(this IServiceCollection services)
    {
        services.AddSingleton<IPracticeScope, PostgresPracticeScope>();
        services.AddSingleton<IAttestationPacks, PostgresAttestationPacks>();
        services.AddSingleton<ICohorts, PostgresCohorts>();
        return services;
    }
}
