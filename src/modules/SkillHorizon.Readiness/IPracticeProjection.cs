using Npgsql;

namespace SkillHorizon.Readiness;

public sealed record PracticeSnapshot(
    string PracticeCode,
    DateTimeOffset AsOf,
    bool Dirty,
    int MemberCount,
    int MeasuredCount,
    int GapCount,
    int CohortCount);

public interface IPracticeProjection
{
    Task MarkDirtyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken);

    Task ProjectAsync(CancellationToken cancellationToken);

    Task<PracticeSnapshot?> ReadAsync(string practiceCode, CancellationToken cancellationToken);
}
