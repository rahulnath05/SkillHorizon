namespace SkillHorizon.Audit;

public sealed record AuditRecordView(
    Guid AuditId,
    string SubjectId,
    string Action,
    string RecordType,
    Guid RecordId,
    DateTimeOffset OccurredAt);

public sealed record ModelRegistration(Guid ModelId, string ModelName, string Purpose, bool Connected);

public sealed record MalpracticeCaseView(Guid CaseId, Guid SittingId, string Status, string OpenedBy, DateTimeOffset OpenedAt);

public sealed record ExportType(string Code, string Description);

public sealed record PersonalMetadataExport(
    Guid ExportId,
    string SubjectId,
    string DisplayName,
    string LegalEntityId,
    string CountryCode,
    string EmploymentStatus,
    IReadOnlyList<string> Workspaces);

public interface IPlatformRegister
{
    Task<IReadOnlyList<AuditRecordView>> ListAuditAsync(string actorId, string correlationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModelRegistration>> ListModelsAsync(CancellationToken cancellationToken);

    Task<MalpracticeCaseView> OpenCaseAsync(string actorId, Guid sittingId, string status, CancellationToken cancellationToken);

    Task<IReadOnlyList<MalpracticeCaseView>> ListCasesAsync(CancellationToken cancellationToken);

    IReadOnlyList<ExportType> ListExportTypes();

    Task<PersonalMetadataExport> ExportPersonalMetadataAsync(
        string actorId,
        string subjectId,
        string correlationId,
        CancellationToken cancellationToken);
}
