using Microsoft.Extensions.DependencyInjection;

namespace SkillHorizon.Taxonomy;

public static class TaxonomyModule
{
    public const string Schema = "taxonomy";

    public static IServiceCollection AddTaxonomyModule(this IServiceCollection services) => services;
}
