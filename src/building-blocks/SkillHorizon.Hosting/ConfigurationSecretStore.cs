using Microsoft.Extensions.Configuration;

namespace SkillHorizon.Hosting;

public sealed class ConfigurationSecretStore : ISecretStore
{
    private readonly IConfiguration _configuration;

    public ConfigurationSecretStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public static string EnvironmentVariableName(string secretName) =>
        "SKILLHORIZON_SECRET_" + secretName.Trim().ToUpperInvariant().Replace('-', '_');

    public Task<string> GetRequiredAsync(string name, CancellationToken cancellationToken)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName(name));
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return Task.FromResult(fromEnvironment);

        var fromConfiguration = _configuration[$"{SkillHorizonOptions.SectionName}:Secrets:{name}"];
        if (!string.IsNullOrWhiteSpace(fromConfiguration))
            return Task.FromResult(fromConfiguration);

        throw new InvalidOperationException(
            $"Secret '{name}' was not found. Set {EnvironmentVariableName(name)} or SkillHorizon:Secrets:{name}.");
    }
}
