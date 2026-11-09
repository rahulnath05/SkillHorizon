using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SkillHorizon.Hosting;

public sealed class LocalDevelopmentIdentityProvider : IIdentityProvider
{
    public const string Issuer = "skillhorizon-local";
    public const string Audience = "skillhorizon";

    private readonly IReadOnlyList<DevelopmentIdentity> _identities;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly JsonWebTokenHandler _tokens = new();

    public LocalDevelopmentIdentityProvider(IOptions<SkillHorizonOptions> options, ISecretStore secrets)
    {
        _identities = DevelopmentIdentityParser.Parse(options.Value.DevIdentities);
        var secret = secrets.GetRequiredAsync(options.Value.SigningKeySecretName, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException(
                $"Secret '{options.Value.SigningKeySecretName}' must be at least 32 bytes.");
        }

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public string Name => "local-development";

    public bool IsLocalDevelopment => true;

    public IReadOnlyList<DevelopmentIdentity> DevelopmentIdentities => _identities;

    public DevelopmentIdentity? FindDevelopmentIdentity(string subjectId) =>
        _identities.FirstOrDefault(identity => identity.SubjectId == subjectId);

    public Task<string> IssueTokenAsync(IdentityAssertion assertion, CancellationToken cancellationToken)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", assertion.SubjectId),
                new Claim("employee_id", assertion.EmployeeId),
                new Claim("legal_entity", assertion.LegalEntityId)
            ])
        };

        return Task.FromResult(_tokens.CreateToken(descriptor));
    }

    public async Task<IdentityAssertion?> ReadBearerAsync(string? bearerToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return null;

        var result = await _tokens.ValidateTokenAsync(bearerToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub"
        }).ConfigureAwait(false);

        return result.IsValid ? ReadAssertion(result.ClaimsIdentity) : null;
    }

    internal static IdentityAssertion? ReadAssertion(ClaimsIdentity? identity)
    {
        if (identity is null)
            return null;

        var subject = identity.FindFirst("sub")?.Value
            ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var employee = identity.FindFirst("employee_id")?.Value;
        var legalEntity = identity.FindFirst("legal_entity")?.Value ?? "";
        return new IdentityAssertion(subject, string.IsNullOrWhiteSpace(employee) ? subject : employee, legalEntity);
    }
}
