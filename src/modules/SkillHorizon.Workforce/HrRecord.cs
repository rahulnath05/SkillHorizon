namespace SkillHorizon.Workforce;

public sealed record HrRecord(
    string? EmployeeId,
    string? LegalEntityId,
    string? CountryCode,
    string? ManagerEmployeeId,
    string? EmploymentStatus,
    bool PersonalLearningAssigned,
    string? DisplayName);
