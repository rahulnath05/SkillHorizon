namespace SkillHorizon.Workforce;

public interface IHrPlacement
{
    Task<bool> AcceptsAsync(string legalEntityId, CancellationToken cancellationToken);
}
