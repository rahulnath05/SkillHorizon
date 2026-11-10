namespace SkillHorizon.Readiness;

public sealed record MeasuredSkill(string SkillCode, string SkillName, int Rank, string Code, string Name);

public sealed record SkillGap(string SkillName, string CurrentName, int CurrentRank, string TargetName, int TargetRank);

public interface IPersonGap
{
    Task<IReadOnlyList<SkillGap>> ApplyAsync(
        Guid authorizationId,
        Guid resultId,
        IReadOnlyList<MeasuredSkill> skills,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SkillGap>> ReadAsync(string subjectId, CancellationToken cancellationToken);
}
