using SkillHorizon.Access;
using SkillHorizon.Activation;
using SkillHorizon.Attestation;
using SkillHorizon.Audit;
using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Readiness;
using SkillHorizon.Taxonomy;
using SkillHorizon.Workforce;

namespace SkillHorizon.Framework.Tests;

public sealed class ModuleSchemaTests
{
    [Fact]
    public void Schemas_match_the_domain_modules()
    {
        Assert.Equal(
            ["workforce", "access", "taxonomy", "job_architecture", "benchmark", "attestation", "activation", "readiness", "audit"],
            [
                WorkforceModule.Schema,
                AccessModule.Schema,
                TaxonomyModule.Schema,
                JobArchitectureModule.Schema,
                BenchmarkModule.Schema,
                AttestationModule.Schema,
                ActivationModule.Schema,
                ReadinessModule.Schema,
                AuditModule.Schema
            ]);
        Assert.Equal(5, Workspaces.All.Count);
        Assert.True(Workspaces.IsKnown(Workspaces.Employee));
        Assert.False(Workspaces.IsKnown("persona"));
    }
}
