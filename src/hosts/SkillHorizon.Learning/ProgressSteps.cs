using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;

namespace SkillHorizon.Learning;

public sealed class ProgressSteps
{
    private readonly NpgsqlDataSource _progress;
    private readonly HttpClient _core;

    public ProgressSteps(NpgsqlDataSource progress, HttpClient core)
    {
        _progress = progress;
        _core = core;
    }

    public async Task CompleteAssessmentAsync(string subjectId, Guid benchmarkVersionId, Guid resultId, CancellationToken cancellationToken)
    {
        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.step (
                step_id, subject_id, provider, title, benchmark_version_id, result_id, status, recorded_at)
            VALUES ($1, $2, 'skillhorizon_assessment', 'SkillHorizon assessment', $3, $4, 'completed', now())
            ON CONFLICT (result_id) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(benchmarkVersionId);
        command.Parameters.AddWithValue(resultId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AssignExternalAsync(string bearer, string workspace, string correlationId, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("A progress step needs a title.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "employee/subject");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("X-Active-Workspace", workspace);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        using var response = await _core.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("This step is refused for the active workspace.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The core API did not accept this person.");
        var body = await response.Content.ReadFromJsonAsync<SubjectBody>(cancellationToken).ConfigureAwait(false);
        var subjectId = body?.SubjectId;
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new InvalidOperationException("The core API did not accept this person.");

        await using var connection = await _progress.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO progress.step (
                step_id, subject_id, provider, title, status, recorded_at)
            VALUES ($1, $2, 'external', $3, 'assigned', now())
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(title.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record SubjectBody(string SubjectId);
}
