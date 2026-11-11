using Npgsql;
using SkillHorizon.Access;

namespace SkillHorizon.Workforce;

public sealed class PostgresWorkforceSync : IWorkforceSync
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IHrFeed _feed;
    private readonly IHrPlacement _placement;

    public PostgresWorkforceSync(NpgsqlDataSource dataSource, IHrFeed feed, IHrPlacement placement)
    {
        _dataSource = dataSource;
        _feed = feed;
        _placement = placement;
    }

    public async Task<WorkforceSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var records = await _feed.ReadAsync(cancellationToken).ConfigureAwait(false);
        return await ApplyAsync(records, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkforceSyncResult> ApplyAsync(IReadOnlyList<HrRecord> records, CancellationToken cancellationToken)
    {
        var (accepted, quarantined) = Classify(records);
        var placed = new List<AcceptedPerson>();
        foreach (var person in accepted)
        {
            if (await _placement.AcceptsAsync(person.LegalEntityId, cancellationToken).ConfigureAwait(false))
                placed.Add(person);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var person in placed)
            await UpsertPerson(connection, transaction, person, cancellationToken).ConfigureAwait(false);

        await ReplaceQuarantine(connection, transaction, quarantined, cancellationToken).ConfigureAwait(false);
        await ReplaceRelationships(connection, transaction, cancellationToken).ConfigureAwait(false);
        await ReconcileGrants(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new WorkforceSyncResult(
            placed.Count,
            quarantined.Count,
            placed.Count(person => person.EmploymentStatus == "closed"));
    }

    private static async Task UpsertPerson(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AcceptedPerson person,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO workforce.person (
                employee_id,
                legal_entity_id,
                country_code,
                manager_employee_id,
                employment_status,
                personal_learning_assigned,
                display_name,
                closed_at,
                projected_at)
            VALUES (
                $1, $2, $3, $4, $5, $6, $7,
                CASE WHEN $5 = 'closed' THEN now() ELSE NULL END,
                now())
            ON CONFLICT (employee_id) DO UPDATE SET
                legal_entity_id = EXCLUDED.legal_entity_id,
                country_code = EXCLUDED.country_code,
                manager_employee_id = EXCLUDED.manager_employee_id,
                employment_status = EXCLUDED.employment_status,
                personal_learning_assigned = EXCLUDED.personal_learning_assigned,
                display_name = EXCLUDED.display_name,
                closed_at = CASE
                    WHEN EXCLUDED.employment_status = 'active' THEN NULL
                    ELSE COALESCE(workforce.person.closed_at, EXCLUDED.closed_at)
                END,
                projected_at = now()
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(person.EmployeeId);
        command.Parameters.AddWithValue(person.LegalEntityId);
        command.Parameters.AddWithValue(person.CountryCode);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)person.ManagerEmployeeId ?? DBNull.Value });
        command.Parameters.AddWithValue(person.EmploymentStatus);
        command.Parameters.AddWithValue(person.PersonalLearningAssigned);
        command.Parameters.AddWithValue(person.DisplayName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceQuarantine(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<QuarantineEntry> quarantined,
        CancellationToken cancellationToken)
    {
        await using (var clear = new NpgsqlCommand("DELETE FROM workforce.quarantine", connection, transaction))
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in quarantined)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO workforce.quarantine (quarantine_id, employee_id, reason, detail, recorded_at)
                VALUES (gen_random_uuid(), $1, $2, $3, now())
                """,
                connection,
                transaction);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)entry.EmployeeId ?? DBNull.Value });
            command.Parameters.AddWithValue(entry.Reason);
            command.Parameters.AddWithValue(entry.Detail);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReplaceRelationships(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM workforce.management_relationship;

            INSERT INTO workforce.management_relationship (manager_employee_id, report_employee_id)
            SELECT manager_employee_id, employee_id
            FROM workforce.person
            WHERE manager_employee_id IS NOT NULL
              AND btrim(manager_employee_id) <> ''
              AND manager_employee_id <> employee_id;
            """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReconcileGrants(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var revokeEmployee = new NpgsqlCommand(
            """
            DELETE FROM access.workspace_grant AS grant_row
            WHERE grant_row.workspace = $1
              AND EXISTS (
                  SELECT 1
                  FROM workforce.person AS person
                  WHERE person.employee_id = grant_row.subject_id
                    AND (
                        person.employment_status <> 'active'
                        OR NOT person.personal_learning_assigned))
            """,
            connection,
            transaction);
        revokeEmployee.Parameters.AddWithValue(Workspaces.Employee);
        await revokeEmployee.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var grantEmployee = new NpgsqlCommand(
            """
            INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
            SELECT gen_random_uuid(), person.employee_id, $1, now()
            FROM workforce.person AS person
            WHERE person.employment_status = 'active'
              AND person.personal_learning_assigned
            ON CONFLICT (subject_id, workspace) DO NOTHING
            """,
            connection,
            transaction);
        grantEmployee.Parameters.AddWithValue(Workspaces.Employee);
        await grantEmployee.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var revokeManager = new NpgsqlCommand(
            """
            DELETE FROM access.workspace_grant AS grant_row
            WHERE grant_row.workspace = $1
              AND EXISTS (
                  SELECT 1
                  FROM workforce.person AS person
                  WHERE person.employee_id = grant_row.subject_id)
              AND NOT EXISTS (
                  SELECT 1
                  FROM workforce.management_relationship AS link
                  JOIN workforce.person AS report ON report.employee_id = link.report_employee_id
                  JOIN workforce.person AS manager ON manager.employee_id = link.manager_employee_id
                  WHERE link.manager_employee_id = grant_row.subject_id
                    AND report.employment_status = 'active'
                    AND manager.employment_status = 'active')
            """,
            connection,
            transaction);
        revokeManager.Parameters.AddWithValue(Workspaces.PeopleManager);
        await revokeManager.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var grantManager = new NpgsqlCommand(
            """
            INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
            SELECT gen_random_uuid(), manager.employee_id, $1, now()
            FROM workforce.person AS manager
            WHERE manager.employment_status = 'active'
              AND EXISTS (
                  SELECT 1
                  FROM workforce.management_relationship AS link
                  JOIN workforce.person AS report ON report.employee_id = link.report_employee_id
                  WHERE link.manager_employee_id = manager.employee_id
                    AND report.employment_status = 'active')
            ON CONFLICT (subject_id, workspace) DO NOTHING
            """,
            connection,
            transaction);
        grantManager.Parameters.AddWithValue(Workspaces.PeopleManager);
        await grantManager.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static (List<AcceptedPerson> Accepted, List<QuarantineEntry> Quarantined) Classify(IReadOnlyList<HrRecord> records)
    {
        var missing = new List<HrRecord>();
        var grouped = new Dictionary<string, List<HrRecord>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var employeeId = record.EmployeeId?.Trim();
            if (string.IsNullOrEmpty(employeeId))
            {
                missing.Add(record);
                continue;
            }

            if (!grouped.TryGetValue(employeeId, out var group))
            {
                group = [];
                grouped[employeeId] = group;
            }

            group.Add(record);
        }

        var accepted = new List<AcceptedPerson>();
        var quarantined = new List<QuarantineEntry>();
        for (var index = 0; index < missing.Count; index++)
            quarantined.Add(new QuarantineEntry(null, "missing_employee_id", "This record has no employee id."));

        foreach (var (employeeId, group) in grouped)
        {
            if (group.Count > 1)
            {
                for (var copy = 0; copy < group.Count; copy++)
                {
                    quarantined.Add(new QuarantineEntry(
                        employeeId,
                        "duplicate_employee_id",
                        "More than one record uses this employee id."));
                }

                continue;
            }

            var record = group[0];
            var legalEntity = record.LegalEntityId?.Trim();
            if (string.IsNullOrEmpty(legalEntity))
            {
                quarantined.Add(new QuarantineEntry(employeeId, "missing_legal_entity", "This record has no legal entity."));
                continue;
            }

            var country = record.CountryCode?.Trim();
            if (string.IsNullOrEmpty(country))
            {
                quarantined.Add(new QuarantineEntry(employeeId, "missing_country", "This record has no country."));
                continue;
            }

            var status = record.EmploymentStatus?.Trim().ToLowerInvariant();
            var storedStatus = status switch
            {
                "active" => "active",
                "leaver" => "closed",
                _ => null
            };
            if (storedStatus is null)
            {
                quarantined.Add(new QuarantineEntry(
                    employeeId,
                    "unrecognized_status",
                    "Employment status is not active or leaver."));
                continue;
            }

            var manager = record.ManagerEmployeeId?.Trim();
            if (string.IsNullOrEmpty(manager) || string.Equals(manager, employeeId, StringComparison.Ordinal))
                manager = null;

            var displayName = record.DisplayName?.Trim();
            if (string.IsNullOrEmpty(displayName))
                displayName = employeeId;

            accepted.Add(new AcceptedPerson(
                employeeId,
                legalEntity,
                country,
                manager,
                storedStatus,
                record.PersonalLearningAssigned,
                displayName));
        }

        return (accepted, quarantined);
    }

    internal sealed record AcceptedPerson(
        string EmployeeId,
        string LegalEntityId,
        string CountryCode,
        string? ManagerEmployeeId,
        string EmploymentStatus,
        bool PersonalLearningAssigned,
        string DisplayName);

    internal sealed record QuarantineEntry(string? EmployeeId, string Reason, string Detail);
}
