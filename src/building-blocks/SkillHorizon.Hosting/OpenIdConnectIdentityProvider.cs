using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SkillHorizon.Hosting;

public sealed class OpenIdConnectIdentityProvider : IIdentityProvider
{
    private readonly OidcOptions _oidc;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;
    private readonly JsonWebTokenHandler _tokens = new();

    public OpenIdConnectIdentityProvider(IOptions<SkillHorizonOptions> options)
    {
        _oidc = options.Value.Oidc;
        if (string.IsNullOrWhiteSpace(_oidc.Authority))
            throw new InvalidOperationException("SkillHorizon:Oidc:Authority is required for the OpenID Connect adapter.");

        var authority = _oidc.Authority.TrimEnd('/');
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            authority + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
    }

    public string Name => "openid-connect";

    public bool IsLocalDevelopment => false;

    public IReadOnlyList<DevelopmentIdentity> DevelopmentIdentities => [];

    public DevelopmentIdentity? FindDevelopmentIdentity(string subjectId) => null;

    public Task<string> IssueTokenAsync(IdentityAssertion assertion, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The corporate identity provider issues tokens. This adapter only reads them.");

    public async Task<IdentityAssertion?> ReadBearerAsync(string? bearerToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return null;

        var metadata = await _configuration.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = [metadata.Issuer, _oidc.Authority.TrimEnd('/')],
            ValidateAudience = !string.IsNullOrWhiteSpace(_oidc.Audience),
            ValidAudience = _oidc.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = metadata.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        var result = await _tokens.ValidateTokenAsync(bearerToken, parameters).ConfigureAwait(false);
        if (!result.IsValid || result.ClaimsIdentity is null)
            return null;

        var subject = result.ClaimsIdentity.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var employee = result.ClaimsIdentity.FindFirst(_oidc.EmployeeIdClaim)?.Value;
        var legalEntity = result.ClaimsIdentity.FindFirst(_oidc.LegalEntityClaim)?.Value ?? "";
        return new IdentityAssertion(subject, string.IsNullOrWhiteSpace(employee) ? subject : employee, legalEntity);
    }
}
