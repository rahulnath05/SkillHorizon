namespace SkillHorizon.Workforce;

public sealed record DirectReport(string EmployeeId, string DisplayName, string LegalEntityId, string CountryCode);

public sealed record ManagementLink(string ManagerEmployeeId, string ReportEmployeeId);

public interface ITeamDirectory
{
    Task<IReadOnlyList<DirectReport>> ListDirectReportsAsync(string managerEmployeeId, CancellationToken cancellationToken);

    Task<ManagementLink?> FindCurrentLinkAsync(string managerEmployeeId, string reportEmployeeId, CancellationToken cancellationToken);
}
