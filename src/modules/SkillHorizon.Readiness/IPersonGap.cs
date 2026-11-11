namespace SkillHorizon.Readiness;

public sealed record MeasuredSkill(string SkillCode, string SkillName, int Rank, string Code, string Name);

public sealed record SkillGap(string SkillName, string CurrentName, int CurrentRank, string TargetName, int TargetRank);

public sealed record PersonSkill(
    string SkillCode,
    string SkillName,
    int CurrentRank,
    string CurrentName,
    int TargetRank,
    string TargetName);

public sealed record PathStepPlan(string SkillCode, string SkillName, string Provider);

public sealed record PathPlan(Guid ResultId, Guid BenchmarkVersionId, IReadOnlyList<PathStepPlan> Steps);

public interface IPersonGap
{
    Task<IReadOnlyList<SkillGap>> ApplyAsync(
        Guid authorizationId,
        Guid resultId,
        IReadOnlyList<MeasuredSkill> skills,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SkillGap>> ReadAsync(string subjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PersonSkill>> ReadSkillsAsync(string subjectId, CancellationToken cancellationToken);

    Task<PathPlan> PlanAsync(string subjectId, CancellationToken cancellationToken);
}
