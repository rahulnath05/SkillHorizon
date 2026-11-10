using System.Net.Http.Headers;
using SkillHorizon.Hosting;

namespace SkillHorizon.Web;

public sealed class LearningApiClient
{
    private readonly HttpClient _http;

    public LearningApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ApiReply> SendAsync(
        HttpMethod method,
        string path,
        string? sittingToken,
        string? bearer,
        string? workspace,
        string? correlationId,
        string? json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(sittingToken))
            request.Headers.TryAddWithoutValidation("X-Sitting-Token", sittingToken);
        if (!string.IsNullOrWhiteSpace(bearer))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (!string.IsNullOrWhiteSpace(workspace))
            request.Headers.TryAddWithoutValidation(SkillHorizonContext.WorkspaceHeader, workspace);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation(SkillHorizonContext.CorrelationHeader, correlationId);
        if (json is not null)
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new ApiReply((int)response.StatusCode, body);
    }
}
