namespace SkillHorizon.Readiness;

public sealed record DeclaredComparison(
    string SubjectId,
    string SkillCode,
    int? DeclaredRank,
    string? DeclaredName,
    int? AssessedRank,
    string? AssessedName);

public interface IDeclaredLevel
{
    Task<DeclaredComparison> DeclareAsync(
        string subjectId,
        string skillCode,
        int declaredRank,
        string declaredName,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeclaredComparison>> ReadAsync(CancellationToken cancellationToken);
}
