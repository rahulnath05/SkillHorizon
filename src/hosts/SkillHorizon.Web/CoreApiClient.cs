using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SkillHorizon.Hosting;

namespace SkillHorizon.Web;

public enum WorkspaceLookupStatus
{
    Ok,
    Refused,
    Unavailable,
    Unauthenticated
}

public sealed record WorkspaceLookup(WorkspaceLookupStatus Status, IReadOnlyList<string> Workspaces)
{
    public static WorkspaceLookup Ok(IReadOnlyList<string> workspaces) => new(WorkspaceLookupStatus.Ok, workspaces);

    public static WorkspaceLookup Refused() => new(WorkspaceLookupStatus.Refused, []);

    public static WorkspaceLookup Unavailable() => new(WorkspaceLookupStatus.Unavailable, []);

    public static WorkspaceLookup Unauthenticated() => new(WorkspaceLookupStatus.Unauthenticated, []);
}

public sealed class CoreApiClient
{
    private readonly HttpClient _http;

    public CoreApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<WorkspaceLookup> GetWorkspacesAsync(string bearer, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "access/workspaces");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation(SkillHorizonContext.CorrelationHeader, correlationId);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return WorkspaceLookup.Refused();
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return WorkspaceLookup.Unauthenticated();
        if ((int)response.StatusCode >= 500)
            return WorkspaceLookup.Unavailable();

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<WorkspaceBody>(cancellationToken).ConfigureAwait(false);
        return WorkspaceLookup.Ok(body?.Workspaces ?? []);
    }

    private sealed record WorkspaceBody(IReadOnlyList<string> Workspaces);
}
