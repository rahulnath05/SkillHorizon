using Microsoft.Extensions.DependencyInjection;
using SkillHorizon.Access;
using SkillHorizon.Activation;
using SkillHorizon.Attestation;
using SkillHorizon.Audit;
using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Readiness;
using SkillHorizon.Taxonomy;
using SkillHorizon.Workforce;

namespace SkillHorizon.Composition;

public static class ModuleRegistration
{
    public static IServiceCollection AddSkillHorizonModules(this IServiceCollection services)
    {
        return services
            .AddSingleton<IHrPlacement, RegistryHrPlacement>()
            .AddWorkforceModule()
            .AddAccessModule()
            .AddTaxonomyModule()
            .AddJobArchitectureModule()
            .AddBenchmarkModule()
            .AddAttestationModule()
            .AddActivationModule()
            .AddReadinessModule()
            .AddAuditModule();
    }
}
