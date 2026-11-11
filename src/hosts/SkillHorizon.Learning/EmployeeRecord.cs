using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SkillHorizon.Learning;

public sealed record EmployeeActivity(int OpenSteps, bool SittingOpen);

public sealed record DocumentView(
    Guid EvidenceId,
    string FileName,
    string ContentType,
    int ByteLength,
    DateTimeOffset RecordedAt,
    string StepTitle);

public sealed record DocumentFile(string FileName, string ContentType, byte[] Content);

public sealed class EmployeeRecord
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _assessment;
    private readonly NpgsqlDataSource _progress;
    private readonly HttpClient _core;
    private readonly IEvidenceFiles _files;

    public EmployeeRecord(NpgsqlDataSource assessment, NpgsqlDataSource progress, HttpClient core, IEvidenceFiles files)
    {
        _assessment = assessment;
        _progress = progress;
        _core = core;
        _files = files;
    }

    public async Task<EmployeeActivity> ReadActivityAsync(string bearer, string workspace, string correlationId, string? requestedSubject, CancellationToken cancellationToken)
    {
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(requestedSubject) && !string.Equals(requestedSubject, subject, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Another person's record is not available in this workspace.");
        var openSteps = await OpenSteps(subject, cancellationToken).ConfigureAwait(false);
        var sittingOpen = await SittingOpen(subject, cancellationToken).ConfigureAwait(false);
        return new EmployeeActivity(openSteps, sittingOpen);
    }

    public async Task<IReadOnlyList<DocumentView>> ListDocumentsAsync(string bearer, string workspace, string correlationId, string? requestedSubject, CancellationToken cancellationToken)
    {
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(requestedSubject) && !string.Equals(requestedSubject, subject, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Another person's record is not available in this workspace.");

        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT evidence.evidence_id, evidence.file_name, evidence.content_type, evidence.byte_length, evidence.recorded_at, step.title
            FROM progress.evidence AS evidence
            JOIN progress.step AS step ON step.step_id = evidence.step_id
            WHERE step.subject_id = $1
            ORDER BY evidence.recorded_at DESC, evidence.file_name
            """,
            connection);
        command.Parameters.AddWithValue(subject);
        var documents = new List<DocumentView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            documents.Add(new DocumentView(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                ReadTime(reader, 4),
                reader.GetString(5)));
        }

        return documents;
    }

    public async Task<DocumentFile> OpenDocumentAsync(string bearer, string workspace, string correlationId, Guid evidenceId, CancellationToken cancellationToken)
    {
        var subject = await Subject(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        string objectKey;
        string fileName;
        string contentType;
        await using (var command = new NpgsqlCommand(
            """
            SELECT evidence.object_key, evidence.file_name, evidence.content_type
            FROM progress.evidence AS evidence
            JOIN progress.step AS step ON step.step_id = evidence.step_id
            WHERE evidence.evidence_id = $1 AND step.subject_id = $2
            """,
            connection))
        {
            command.Parameters.AddWithValue(evidenceId);
            command.Parameters.AddWithValue(subject);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Another person's record is not available in this workspace.");
            objectKey = reader.GetString(0);
            fileName = reader.GetString(1);
            contentType = reader.GetString(2);
        }

        var content = await _files.ReadAsync(objectKey, cancellationToken).ConfigureAwait(false);
        if (content is null)
            throw new InvalidOperationException("That file is not stored for this person.");
        return new DocumentFile(fileName, contentType, content);
    }

    private async Task<int> OpenSteps(string subjectId, CancellationToken cancellationToken)
    {
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM progress.step AS step
            WHERE step.subject_id = $1
              AND step.status = 'assigned'
              AND step.path_id = (
                  SELECT latest.path_id
                  FROM progress.path AS latest
                  WHERE latest.subject_id = $1
                  ORDER BY latest.created_at DESC
                  LIMIT 1)
            """,
            connection);
        command.Parameters.AddWithValue(subjectId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<bool> SittingOpen(string subjectId, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM assessment.sitting WHERE subject_id = $1 AND status = 'open')",
            connection);
        command.Parameters.AddWithValue(subjectId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<string> Subject(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "employee/subject");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("X-Active-Workspace", workspace);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        using var response = await _core.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("This command is refused for the active workspace.");
        var body = await response.Content.ReadFromJsonAsync<SubjectBody>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("This command is refused for the active workspace.");
        return body.SubjectId;
    }

    private static DateTimeOffset ReadTime(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
            _ => throw new InvalidOperationException("A document timestamp could not be read.")
        };
    }

    private sealed record SubjectBody(string SubjectId);
}
