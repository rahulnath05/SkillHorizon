namespace SkillHorizon.Hosting;

public sealed record IdentityAssertion(string SubjectId, string EmployeeId, string LegalEntityId);

public sealed record DevelopmentIdentity(string SubjectId, string LegalEntityId, string DisplayName);

public interface IIdentityProvider
{
    string Name { get; }

    bool IsLocalDevelopment { get; }

    IReadOnlyList<DevelopmentIdentity> DevelopmentIdentities { get; }

    DevelopmentIdentity? FindDevelopmentIdentity(string subjectId);

    Task<string> IssueTokenAsync(IdentityAssertion assertion, CancellationToken cancellationToken);

    Task<IdentityAssertion?> ReadBearerAsync(string? bearerToken, CancellationToken cancellationToken);
}
