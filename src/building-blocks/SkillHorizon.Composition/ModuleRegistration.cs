using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SkillHorizon.Access;
using SkillHorizon.Activation;
using SkillHorizon.Attestation;
using SkillHorizon.Audit;
using SkillHorizon.Benchmark;
using SkillHorizon.Hosting;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Readiness;
using SkillHorizon.Taxonomy;
using SkillHorizon.Workforce;

namespace SkillHorizon.Composition;

public static class ModuleRegistration
{
    public static IServiceCollection AddSkillHorizonModules(this IServiceCollection services)
    {
        services
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
        services.AddSingleton<IProgressCensus>(provider =>
        {
            var connectionString = provider.GetRequiredService<IOptions<SkillHorizonOptions>>().Value.ProgressConnectionString;
            return string.IsNullOrWhiteSpace(connectionString)
                ? new EmptyProgressCensus()
                : new PostgresProgressCensus(connectionString);
        });
        return services;
    }
}
