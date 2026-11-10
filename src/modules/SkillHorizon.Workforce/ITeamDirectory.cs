namespace SkillHorizon.Workforce;

public sealed record DirectReport(string EmployeeId, string DisplayName, string LegalEntityId, string CountryCode);

public interface ITeamDirectory
{
    Task<IReadOnlyList<DirectReport>> ListDirectReportsAsync(string managerEmployeeId, CancellationToken cancellationToken);
}
