namespace SkillHorizon.Benchmark;

public sealed record TargetDraft(Guid SkillId, int ProficiencyRank);

public sealed record BenchmarkDraftWrite(Guid JobProfileId, Guid TaxonomyVersionId, IReadOnlyList<TargetDraft> Targets);

public sealed record TargetView(
    Guid SkillId,
    string SkillCode,
    string SkillName,
    string CompetencyName,
    string PracticeName,
    int ProficiencyRank,
    string ProficiencyCode,
    string ProficiencyName);

public sealed record BenchmarkVersionView(
    Guid BenchmarkVersionId,
    Guid JobProfileId,
    string ProfileCode,
    string ProfileName,
    string FrameworkCode,
    string FrameworkName,
    string LevelCode,
    string LevelName,
    int LevelRank,
    Guid TaxonomyVersionId,
    int VersionNumber,
    string Status,
    Guid? Supersedes,
    Guid? SupersededBy,
    IReadOnlyList<TargetView> Targets);

public sealed record PublishedTarget(
    Guid SkillId,
    string SkillCode,
    string SkillName,
    string CompetencyName,
    string PracticeName,
    int ProficiencyRank,
    string ProficiencyCode,
    string ProficiencyName);

public sealed record BenchmarkPublication(
    string Kind,
    Guid BenchmarkVersionId,
    Guid JobProfileId,
    string ProfileCode,
    string ProfileName,
    string FrameworkCode,
    string FrameworkName,
    string LevelCode,
    string LevelName,
    int LevelRank,
    Guid TaxonomyVersionId,
    int VersionNumber,
    DateTimeOffset PublishedAt,
    Guid? Supersedes,
    IReadOnlyList<PublishedTarget> Targets);

public interface IBenchmarkAuthoring
{
    Task<BenchmarkVersionView> SaveDraftAsync(BenchmarkDraftWrite draft, CancellationToken cancellationToken);

    Task<BenchmarkVersionView> PublishAsync(Guid benchmarkVersionId, string subjectId, string correlationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BenchmarkVersionView>> ListDraftsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BenchmarkVersionView>> PublishedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BenchmarkVersionView>> HistoryAsync(Guid jobProfileId, CancellationToken cancellationToken);
}

public interface IReferenceLibrary
{
    Task<int> ApplyPendingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BenchmarkVersionView>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BenchmarkVersionView>> HistoryAsync(Guid jobProfileId, CancellationToken cancellationToken);
}
