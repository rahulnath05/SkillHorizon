using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Benchmark;

public static class BenchmarkModule
{
    public const string Schema = "benchmark";

    public static IServiceCollection AddBenchmarkModule(this IServiceCollection services) => services;
}
