using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace SkillHorizon.Hosting;

public static class HostBuilderExtensions
{
    public static SkillHorizonOptions BindSkillHorizonOptions(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<SkillHorizonOptions>()
            .Bind(builder.Configuration.GetSection(SkillHorizonOptions.SectionName));
        var options = builder.Configuration.GetSection(SkillHorizonOptions.SectionName).Get<SkillHorizonOptions>() ?? new SkillHorizonOptions();
        if (string.IsNullOrWhiteSpace(options.ZoneId))
            throw new InvalidOperationException("SkillHorizon:ZoneId is required.");

        return options;
    }

    public static IServiceCollection AddSkillHorizonSecrets(this IServiceCollection services)
    {
        services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
        return services;
    }

    public static IServiceCollection AddSkillHorizonIdentity(this IServiceCollection services, SkillHorizonOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Oidc.Authority))
            services.AddSingleton<IIdentityProvider, LocalDevelopmentIdentityProvider>();
        else
            services.AddSingleton<IIdentityProvider, OpenIdConnectIdentityProvider>();

        return services;
    }

    public static IServiceCollection AddSkillHorizonZoneDirectory(this IServiceCollection services, SkillHorizonOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ControlRegistryUrl))
            throw new InvalidOperationException("SkillHorizon:ControlRegistryUrl is required.");

        services.AddMemoryCache();
        services.AddHttpClient<IZoneDirectory, ControlRegistryClient>((serviceProvider, client) =>
        {
            var configured = serviceProvider.GetRequiredService<IOptions<SkillHorizonOptions>>().Value.ControlRegistryUrl;
            client.BaseAddress = new Uri(configured.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        return services;
    }
}
