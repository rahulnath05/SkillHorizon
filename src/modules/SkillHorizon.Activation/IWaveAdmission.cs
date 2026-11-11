namespace SkillHorizon.Activation;

public sealed record WaveAdmission(Guid WaveId, Guid BenchmarkVersionId, string ProfileName, int Population);

public sealed record WaveListing(
    Guid WaveId,
    Guid BenchmarkVersionId,
    string ProfileName,
    int Population,
    int AdmittedCount,
    DateTimeOffset OpenedAt,
    IReadOnlyList<string> SubjectIds);

public sealed record ActivationCentre(int? SittingBudget, IReadOnlyList<WaveListing> Waves);

public sealed record ZonePerson(string EmployeeId, string LegalEntityId, string EmploymentStatus, DateTimeOffset? JoinedAt);

public sealed record AuthorizedSkill(
    Guid SkillId,
    string SkillCode,
    string SkillName,
    int TargetRank,
    string TargetCode,
    string TargetName);

public sealed record AuthorizedLevel(int Rank, string Code, string Name);

public sealed record SittingGrant(
    Guid AuthorizationId,
    string SubjectId,
    Guid WaveId,
    Guid BenchmarkVersionId,
    IReadOnlyList<AuthorizedSkill> Skills,
    IReadOnlyList<AuthorizedLevel> Scale);

public sealed record IssuedSitting(SittingGrant Grant, string Token);

public interface IWaveAdmission
{
    Task EnsureBudgetAsync(string zoneId, int sittingBudget, CancellationToken cancellationToken);

    Task SetBudgetAsync(string zoneId, int sittingBudget, CancellationToken cancellationToken);

    Task<WaveAdmission> AdmitAsync(
        string zoneId,
        Guid benchmarkVersionId,
        IReadOnlyList<string> subjectIds,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<ActivationCentre> ReadCentreAsync(string zoneId, CancellationToken cancellationToken);

    Task<ZonePerson?> FindPersonAsync(string employeeId, CancellationToken cancellationToken);

    Task<WaveAdmission> AdmitRollingAsync(
        string zoneId,
        Guid waveId,
        string subjectId,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<IssuedSitting> AuthorizeAsync(Guid waveId, string subjectId, CancellationToken cancellationToken);

    Task<SittingGrant?> ReadGrantAsync(string token, CancellationToken cancellationToken);

    Task<WaveAdmission?> CurrentWaveAsync(string subjectId, CancellationToken cancellationToken);
}
