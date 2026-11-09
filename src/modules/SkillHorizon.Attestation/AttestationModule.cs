using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Attestation;

public static class AttestationModule
{
    public const string Schema = "attestation";

    public static IServiceCollection AddAttestationModule(this IServiceCollection services) => services;
}
