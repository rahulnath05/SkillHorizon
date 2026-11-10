namespace SkillHorizon.JobArchitecture;

public sealed record LevelImport(string Code, string Name, int Rank);

public sealed record ProfileImport(string Code, string Name, string LevelCode);

public sealed record FrameworkImport(
    string Code,
    string Name,
    IReadOnlyList<LevelImport> Levels,
    IReadOnlyList<ProfileImport> Profiles);

public sealed record FrameworkLevel(Guid LevelId, string Code, string Name, int Rank);

public sealed record FrameworkProfile(Guid ProfileId, string Code, string Name, string LevelCode, string LevelName, int LevelRank);

public sealed record FrameworkView(
    Guid FrameworkId,
    string Code,
    string Name,
    IReadOnlyList<FrameworkLevel> Levels,
    IReadOnlyList<FrameworkProfile> Profiles);

public sealed record JobProfileDescriptor(
    Guid ProfileId,
    string ProfileCode,
    string ProfileName,
    string FrameworkCode,
    string FrameworkName,
    string LevelCode,
    string LevelName,
    int LevelRank);

public interface IJobArchitecture
{
    Task ImportAsync(IReadOnlyList<FrameworkImport> frameworks, CancellationToken cancellationToken);

    Task<IReadOnlyList<FrameworkView>> ListAsync(CancellationToken cancellationToken);

    Task<JobProfileDescriptor?> FindProfileAsync(Guid profileId, CancellationToken cancellationToken);
}
