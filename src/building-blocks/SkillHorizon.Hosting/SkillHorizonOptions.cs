namespace SkillHorizon.Hosting;

public sealed class SkillHorizonOptions
{
    public const string SectionName = "SkillHorizon";

    public string ZoneId { get; set; } = "";

    public string DomainConnectionString { get; set; } = "";

    public string ControlConnectionString { get; set; } = "";

    public string Redis { get; set; } = "";

    public string ControlRegistryUrl { get; set; } = "";

    public string ApiUrl { get; set; } = "";

    public string SigningKeySecretName { get; set; } = "local-signing-key";

    public string RegistryKeySecretName { get; set; } = "registry-key";

    public string DevIdentities { get; set; } = "";

    public bool MigrateOnStartup { get; set; } = true;

    public bool EnableConsoleExporter { get; set; } = true;

    public bool PublishOutbox { get; set; } = true;

    public OidcOptions Oidc { get; set; } = new();
}

public sealed class OidcOptions
{
    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string Audience { get; set; } = "";

    public string ClientSecretName { get; set; } = "oidc-client-secret";

    public string CallbackPath { get; set; } = "/signin-oidc";

    public string LegalEntityClaim { get; set; } = "legal_entity";

    public string EmployeeIdClaim { get; set; } = "employee_id";
}
