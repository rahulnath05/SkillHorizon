using System.Text.Json;
using Npgsql;

namespace SkillHorizon.Audit;

public sealed class PostgresPlatformRegister : IPlatformRegister
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ExportType[] Types =
    [
        new("personal_metadata", "Identity and employment fields for one person."),
        new("aggregate_counts", "Practice counts with no person rows."),
        new("taxonomy_version", "A published taxonomy version.")
    ];

    private static readonly HashSet<string> CaseStatuses = new(StringComparer.Ordinal)
    {
        "open",
        "explained",
        "set_aside"
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;

    static PostgresPlatformRegister()
    {
        if (Types.Any(type => type.Code.Contains("response", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Response text is not an export type.");
    }

    public PostgresPlatformRegister(NpgsqlDataSource dataSource, IAuditLog audit)
    {
        _dataSource = dataSource;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AuditRecordView>> ListAuditAsync(string actorId, string correlationId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(actorId, "audit.listed", "audit.audit_record", Guid.NewGuid(), correlationId, "{\"listed\":1}"),
            cancellationToken).ConfigureAwait(false);

        var records = new List<AuditRecordView>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT audit_id, subject_id, action, record_type, record_id, occurred_at
            FROM audit.audit_record
            ORDER BY occurred_at DESC
            LIMIT 200
            """,
            connection,
            transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(new AuditRecordView(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return records;
    }

    public async Task<IReadOnlyList<ModelRegistration>> ListModelsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT model_id, model_name, purpose, connected
            FROM audit.model_register
            WHERE connected
            ORDER BY model_name
            """,
            connection);
        var models = new List<ModelRegistration>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            models.Add(new ModelRegistration(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));

        return models;
    }

    public async Task<MalpracticeCaseView> OpenCaseAsync(string actorId, Guid sittingId, string status, CancellationToken cancellationToken)
    {
        var code = status.Trim();
        if (sittingId == Guid.Empty || !CaseStatuses.Contains(code))
            throw new InvalidOperationException("A case stores a status and a sitting id.");

        var caseId = Guid.NewGuid();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.malpractice_case (case_id, sitting_id, status, opened_by, opened_at)
            VALUES ($1, $2, $3, $4, now())
            RETURNING opened_at
            """,
            connection);
        command.Parameters.AddWithValue(caseId);
        command.Parameters.AddWithValue(sittingId);
        command.Parameters.AddWithValue(code);
        command.Parameters.AddWithValue(actorId);
        DateTimeOffset openedAt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            openedAt = reader.GetFieldValue<DateTimeOffset>(0);
        }

        return new MalpracticeCaseView(caseId, sittingId, code, actorId, openedAt);
    }

    public async Task<IReadOnlyList<MalpracticeCaseView>> ListCasesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT case_id, sitting_id, status, opened_by, opened_at
            FROM audit.malpractice_case
            ORDER BY opened_at DESC
            """,
            connection);
        var cases = new List<MalpracticeCaseView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cases.Add(new MalpracticeCaseView(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return cases;
    }

    public IReadOnlyList<ExportType> ListExportTypes() => Types;

    public async Task<PersonalMetadataExport> ExportPersonalMetadataAsync(
        string actorId,
        string subjectId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subject = subjectId.Trim();
        if (subject.Length is 0 or > 80)
            throw new InvalidOperationException("A personal export names a person.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string displayName;
        string legalEntityId;
        string countryCode;
        string employmentStatus;
        await using (var person = new NpgsqlCommand(
            """
            SELECT display_name, legal_entity_id, country_code, employment_status
            FROM workforce.person
            WHERE employee_id = $1
            """,
            connection,
            transaction))
        {
            person.Parameters.AddWithValue(subject);
            await using var reader = await person.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("That person is not in this zone.");
            displayName = reader.GetString(0);
            legalEntityId = reader.GetString(1);
            countryCode = reader.GetString(2);
            employmentStatus = reader.GetString(3);
        }

        var workspaces = new List<string>();
        await using (var grants = new NpgsqlCommand(
            """
            SELECT workspace
            FROM access.workspace_grant
            WHERE subject_id = $1
            ORDER BY workspace
            """,
            connection,
            transaction))
        {
            grants.Parameters.AddWithValue(subject);
            await using var reader = await grants.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                workspaces.Add(reader.GetString(0));
        }

        var exportId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO audit.personal_export (export_id, subject_id, requested_by, requested_at)
            VALUES ($1, $2, $3, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(exportId);
            insert.Parameters.AddWithValue(subject);
            insert.Parameters.AddWithValue(actorId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(
                actorId,
                "export.personal_metadata",
                "audit.personal_export",
                exportId,
                correlationId,
                JsonSerializer.Serialize(new { subjectId = subject }, Json)),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PersonalMetadataExport(exportId, subject, displayName, legalEntityId, countryCode, employmentStatus, workspaces);
    }
}
