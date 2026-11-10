namespace SkillHorizon.Workforce;

public interface IHrFeed
{
    Task<IReadOnlyList<HrRecord>> ReadAsync(CancellationToken cancellationToken);
}
