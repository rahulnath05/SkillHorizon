namespace SkillHorizon.Workforce;

public sealed record WorkforceSyncResult(int Projected, int Quarantined, int Closed);

public interface IWorkforceSync
{
    Task<WorkforceSyncResult> SyncAsync(CancellationToken cancellationToken);

    Task<WorkforceSyncResult> ApplyAsync(IReadOnlyList<HrRecord> records, CancellationToken cancellationToken);
}
