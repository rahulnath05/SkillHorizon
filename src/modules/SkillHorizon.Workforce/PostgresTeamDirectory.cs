using Npgsql;

namespace SkillHorizon.Workforce;

public sealed class PostgresTeamDirectory : ITeamDirectory
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresTeamDirectory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<DirectReport>> ListDirectReportsAsync(
        string managerEmployeeId,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT report.employee_id, report.display_name, report.legal_entity_id, report.country_code
            FROM workforce.management_relationship AS link
            JOIN workforce.person AS report ON report.employee_id = link.report_employee_id
            WHERE link.manager_employee_id = $1
              AND report.employment_status = 'active'
            ORDER BY report.display_name, report.employee_id
            """);
        command.Parameters.AddWithValue(managerEmployeeId);
        var people = new List<DirectReport>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            people.Add(new DirectReport(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return people;
    }

    public async Task<ManagementLink?> FindCurrentLinkAsync(
        string managerEmployeeId,
        string reportEmployeeId,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT link.manager_employee_id, link.report_employee_id
            FROM workforce.management_relationship AS link
            JOIN workforce.person AS report ON report.employee_id = link.report_employee_id
            WHERE link.manager_employee_id = $1
              AND link.report_employee_id = $2
              AND report.employment_status = 'active'
            """);
        command.Parameters.AddWithValue(managerEmployeeId);
        command.Parameters.AddWithValue(reportEmployeeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new ManagementLink(reader.GetString(0), reader.GetString(1));
    }
}
