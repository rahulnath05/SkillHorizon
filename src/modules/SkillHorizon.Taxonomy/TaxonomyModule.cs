using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Taxonomy;

public static class TaxonomyModule
{
    public const string Schema = "taxonomy";

    public const string PublishedMessage = "taxonomy.published";

    public static IServiceCollection AddTaxonomyModule(this IServiceCollection services)
    {
        services.AddSingleton<ITaxonomyAuthoring, PostgresTaxonomyAuthoring>();
        return services;
    }
}
