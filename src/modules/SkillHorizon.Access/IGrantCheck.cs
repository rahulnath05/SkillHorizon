namespace SkillHorizon.Access;

public interface IGrantCheck
{
    Task<bool> HasGrantAsync(string subjectId, string workspace, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListWorkspacesAsync(string subjectId, CancellationToken cancellationToken);
}
