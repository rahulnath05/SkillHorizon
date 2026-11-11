namespace SkillHorizon.Readiness;

public sealed record ProgressCounts(int PathCount, int StepCount, int OpenStepCount);

public interface IProgressCensus
{
    Task<ProgressCounts> CountAsync(IReadOnlyCollection<string> subjectIds, CancellationToken cancellationToken);
}
