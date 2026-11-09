namespace SkillHorizon.Hosting;

public interface ISecretStore
{
    Task<string> GetRequiredAsync(string name, CancellationToken cancellationToken);
}
