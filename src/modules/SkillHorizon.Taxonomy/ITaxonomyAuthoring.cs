namespace SkillHorizon.Taxonomy;

public sealed record SkillDraft(string Code, string Name);

public sealed record CompetencyDraft(string Code, string Name, IReadOnlyList<SkillDraft> Skills);

public sealed record PracticeDraft(string Code, string Name, IReadOnlyList<CompetencyDraft> Competencies);

public sealed record ProficiencyDraft(string Code, string Name, int Rank);

public sealed record TaxonomyDraft(IReadOnlyList<PracticeDraft> Practices, IReadOnlyList<ProficiencyDraft> Proficiencies);

public sealed record TaxonomySkillView(
    Guid SkillId,
    string Code,
    string Name,
    string CompetencyCode,
    string CompetencyName,
    string PracticeCode,
    string PracticeName);

public sealed record ProficiencyView(string Code, string Name, int Rank);

public sealed record TaxonomyVersionView(
    Guid TaxonomyVersionId,
    int VersionNumber,
    string Status,
    IReadOnlyList<TaxonomySkillView> Skills,
    IReadOnlyList<ProficiencyView> Proficiencies);

public sealed record PublishedSkill(
    Guid SkillId,
    string SkillCode,
    string SkillName,
    string CompetencyCode,
    string CompetencyName,
    string PracticeCode,
    string PracticeName);

public sealed record PublishedProficiency(string Code, string Name, int Rank);

public sealed record TaxonomyPublication(
    string Kind,
    Guid TaxonomyVersionId,
    int VersionNumber,
    DateTimeOffset PublishedAt,
    Guid? Supersedes,
    IReadOnlyList<PublishedSkill> Skills,
    IReadOnlyList<PublishedProficiency> Proficiencies);

public interface ITaxonomyAuthoring
{
    Task<TaxonomyVersionView> SaveDraftAsync(TaxonomyDraft draft, CancellationToken cancellationToken);

    Task<TaxonomyVersionView> PublishAsync(Guid taxonomyVersionId, string subjectId, string correlationId, CancellationToken cancellationToken);

    Task<TaxonomyVersionView?> FindAsync(Guid taxonomyVersionId, CancellationToken cancellationToken);
}
