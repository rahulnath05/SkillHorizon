using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Benchmark;

public static class BenchmarkModule
{
    public const string Schema = "benchmark";

    public const string PublishedMessage = "benchmark.published";

    public const string MarketPublishedMessage = "market.published";

    public static IServiceCollection AddBenchmarkModule(this IServiceCollection services)
    {
        services.AddSingleton<IBenchmarkAuthoring, PostgresBenchmarkAuthoring>();
        services.AddSingleton<IMarketDemand, PostgresMarketDemand>();
        services.AddSingleton<IReferenceLibrary, PostgresReferenceLibrary>();
        return services;
    }
}
