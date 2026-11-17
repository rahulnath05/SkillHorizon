namespace SkillHorizon.Access;

public sealed record WorkspaceGrantRow(Guid GrantId, string SubjectId, string Workspace, DateTimeOffset GrantedAt);

public interface IWorkspaceAdministration
{
    Task<IReadOnlyList<WorkspaceGrantRow>> ListAsync(CancellationToken cancellationToken);

    Task<WorkspaceGrantRow> GrantAsync(
        string actorId,
        string subjectId,
        string workspace,
        string correlationId,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        string actorId,
        string subjectId,
        string workspace,
        string correlationId,
        CancellationToken cancellationToken);
}
