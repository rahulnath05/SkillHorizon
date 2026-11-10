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

    public async Task<TeamLookup> GetTeamAsync(string bearer, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "workforce/team");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation(SkillHorizonContext.WorkspaceHeader, "people_manager");
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation(SkillHorizonContext.CorrelationHeader, correlationId);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return TeamLookup.Refused();
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return TeamLookup.Unauthenticated();
        if ((int)response.StatusCode >= 500)
            return TeamLookup.Unavailable();

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TeamBody>(cancellationToken).ConfigureAwait(false);
        return TeamLookup.Ok(body?.People ?? []);
    }

    public async Task<ApiReply> SendAsync(
        HttpMethod method,
        string path,
        string workspace,
        string bearer,
        string correlationId,
        string? json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation(SkillHorizonContext.WorkspaceHeader, workspace);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation(SkillHorizonContext.CorrelationHeader, correlationId);
        if (json is not null)
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new ApiReply((int)response.StatusCode, body);
    }

    private sealed record WorkspaceBody(IReadOnlyList<string> Workspaces);

    private sealed record TeamBody(IReadOnlyList<TeamPerson> People);
}

public enum TeamLookupStatus
{
    Ok,
    Refused,
    Unavailable,
    Unauthenticated
}

public sealed record TeamPerson(string EmployeeId, string DisplayName, string LegalEntityId, string CountryCode);

public sealed record ApiReply(int Status, string Body);

public sealed record TeamLookup(TeamLookupStatus Status, IReadOnlyList<TeamPerson> People)
{
    public static TeamLookup Ok(IReadOnlyList<TeamPerson> people) => new(TeamLookupStatus.Ok, people);

    public static TeamLookup Refused() => new(TeamLookupStatus.Refused, []);

    public static TeamLookup Unavailable() => new(TeamLookupStatus.Unavailable, []);

    public static TeamLookup Unauthenticated() => new(TeamLookupStatus.Unauthenticated, []);
}
