namespace SkillHorizon.Attestation;

public sealed record PackRowWrite(Guid JobProfileId, string Classification, string? ExceptionNote);

public sealed record PackRowView(
    Guid RowId,
    Guid JobProfileId,
    string ProfileCode,
    string ProfileName,
    string Classification,
    string? ExceptionNote,
    Guid? BenchmarkVersionId,
    DateTimeOffset? ReviewedAt,
    string? Decision);

public sealed record PackView(
    Guid PackId,
    string PracticeCode,
    string Status,
    string SubmittedBy,
    Guid? TaxonomyVersionId,
    string? LeadSubjectId,
    DateTimeOffset? SealedAt,
    IReadOnlyList<PackRowView> Rows);

public sealed record CohortView(
    Guid CohortId,
    string PracticeCode,
    string? CountryCode,
    string? OrgUnit,
    Guid JobProfileId,
    int MemberCount);

public interface IPracticeScope
{
    Task<bool> GrantAsync(string subjectId, string practiceCode, CancellationToken cancellationToken);

    Task<bool> HoldsAsync(string subjectId, string practiceCode, CancellationToken cancellationToken);
}

public interface IAttestationPacks
{
    Task<PackView> SubmitAsync(
        string subjectId,
        string correlationId,
        string practiceCode,
        IReadOnlyList<PackRowWrite> rows,
        CancellationToken cancellationToken);

    Task<PackView> MarkReviewedAsync(
        string subjectId,
        string correlationId,
        Guid packId,
        IReadOnlyList<Guid> rowIds,
        CancellationToken cancellationToken);

    Task<PackView> DecideAsync(
        string subjectId,
        string correlationId,
        Guid packId,
        Guid rowId,
        string decision,
        CancellationToken cancellationToken);

    Task<PackView> SealAsync(string subjectId, string correlationId, Guid packId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PackView>> ListAsync(string? practiceCode, CancellationToken cancellationToken);
}

public interface ICohorts
{
    Task SetMaximumAsync(string zoneId, int maximum, CancellationToken cancellationToken);

    Task<CohortView> CreateAsync(
        string zoneId,
        string subjectId,
        string correlationId,
        string practiceCode,
        string? countryCode,
        string? orgUnit,
        Guid jobProfileId,
        IReadOnlyList<string> memberIds,
        CancellationToken cancellationToken);
}
