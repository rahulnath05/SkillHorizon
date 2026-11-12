using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkillHorizon.Messaging;
using StackExchange.Redis;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class FrameworkProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;

    public FrameworkProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Unmapped_person_is_refused_and_the_log_line_shows_zone_and_correlation_id()
    {
        var correlationId = "proof-" + Guid.NewGuid().ToString("N");
        var token = await _infrastructure.IssueTokenAsync("person-unmapped", "LE-OTHER");
        using var client = _infrastructure.Api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/access/workspaces");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Unmapped person", body);
        Assert.Contains(
            _infrastructure.Logs.Messages,
            message => message.Contains("Unmapped person refused.", StringComparison.Ordinal)
                && message.Contains("zone=local", StringComparison.Ordinal)
                && message.Contains("correlation_id=" + correlationId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Command_without_a_grant_is_refused_and_a_held_grant_is_accepted()
    {
        using var client = _infrastructure.Api.CreateClient();

        var ungranted = await SendCommand(client, "person-nogrant", "LE-LOCAL", "employee");
        Assert.Equal(HttpStatusCode.Forbidden, ungranted.StatusCode);
        Assert.Contains("Grant required", await ungranted.Content.ReadAsStringAsync());

        var wrongWorkspace = await SendCommand(client, "person-single", "LE-LOCAL", "people_manager");
        Assert.Equal(HttpStatusCode.Forbidden, wrongWorkspace.StatusCode);

        var held = await SendCommand(client, "person-single", "LE-LOCAL", "employee");
        Assert.Equal(HttpStatusCode.NoContent, held.StatusCode);
    }

    [Fact]
    public async Task Worker_publishes_an_outbox_row()
    {
        var correlationId = "outbox-" + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_infrastructure.DomainConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO messaging.outbox (outbox_id, zone_id, message_type, payload, correlation_id, occurred_at)
                VALUES ($1, 'local', 'framework.probe', '{}'::jsonb, $2, now())
                """,
                connection);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(correlationId);
            await command.ExecuteNonQueryAsync();
        }

        var publisher = _infrastructure.Worker.Services.GetRequiredService<IOutboxPublisher>();
        var published = await publisher.PublishPendingAsync(CancellationToken.None);
        Assert.True(published >= 1);

        await using var check = new NpgsqlConnection(_infrastructure.DomainConnection);
        await check.OpenAsync();
        await using var read = new NpgsqlCommand("SELECT published_at IS NOT NULL FROM messaging.outbox WHERE outbox_id = $1", check);
        read.Parameters.AddWithValue(id);
        var marked = await read.ExecuteScalarAsync();
        Assert.Equal(true, marked);

        var redis = _infrastructure.Worker.Services.GetRequiredService<IConnectionMultiplexer>();
        var values = await redis.GetDatabase().ListRangeAsync("skillhorizon:local:outbox");
        Assert.Contains(values, value => value.ToString().Contains(correlationId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_host_answers_health()
    {
        Assert.Equal(HttpStatusCode.OK, (await _infrastructure.Api.CreateClient().GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _infrastructure.Worker.CreateClient().GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _infrastructure.Control.CreateClient().GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _infrastructure.Web.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Web_shell_refuses_an_unmapped_person_and_asks_when_several_workspaces_are_held()
    {
        using var anonymous = Client();
        var signInPage = await anonymous.GetAsync("/signin");
        var signInHtml = await signInPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, signInPage.StatusCode);
        Assert.Contains("id=\"root\"", signInHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.css", signInHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Plus Jakarta", signInHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/shell.css")).StatusCode);

        foreach (var path in new[] { "/", "/signin", "/entry", "/workspace", "/refused" })
        {
            var page = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        }

        var bundle = await ClientBundle(anonymous, signInHtml);
        Assert.Contains("#112b27", bundle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#f3f1eb", bundle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose a workspace", bundle, StringComparison.Ordinal);
        Assert.Contains("not mapped to a zone", bundle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active workspace", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("Aarav Mehta", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("switch-persona", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("choice-title", bundle, StringComparison.Ordinal);

        var directory = await ReadShell(anonymous);
        Assert.False(directory.Authenticated);
        Assert.Equal(
            ["person-multi", "person-single", "person-unmapped", "person-nogrant", "person-ld", "person-admin"],
            directory.Identities.Select(identity => identity.SubjectId).ToArray());
        Assert.Empty(directory.Workspaces);

        using var unmapped = Client();
        var refused = await SignIn(unmapped, "person-unmapped");
        Assert.Equal("refused", refused.Status);
        Assert.Equal("unmapped", refused.Reason);
        Assert.Empty(refused.Workspaces);
        Assert.Null(refused.ActiveWorkspace);
        Assert.Equal("/refused?reason=unmapped", Destination(refused));

        using var several = Client();
        var entry = await SignIn(several, "person-multi");
        Assert.Equal(["employee", "people_manager"], entry.Workspaces);
        Assert.Null(entry.ActiveWorkspace);
        Assert.Equal("/entry", Destination(entry));
        Assert.DoesNotContain("platform_admin", entry.Workspaces);
        Assert.DoesNotContain("competency_lead", entry.Workspaces);
        Assert.DoesNotContain("learning_and_development", entry.Workspaces);

        var denied = await Post(several, "/api/workspace", new { workspace = "platform_admin" }, entry.RequestToken);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Contains("not held", await denied.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Null((await ReadShell(several)).ActiveWorkspace);

        var chosen = await Post(several, "/api/workspace", new { workspace = "people_manager" }, (await ReadShell(several)).RequestToken);
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        var active = await ReadShell(several);
        Assert.Equal("people_manager", active.ActiveWorkspace);
        Assert.Equal(["employee", "people_manager"], active.Workspaces);
        Assert.Equal("/workspace", Destination(active));

        using var single = Client();
        var direct = await SignIn(single, "person-single");
        Assert.Equal(["employee"], direct.Workspaces);
        Assert.Equal("employee", direct.ActiveWorkspace);
        Assert.Equal("/workspace", Destination(direct));
        Assert.DoesNotContain("people_manager", direct.Workspaces);
    }

    private HttpClient Client() =>
        _infrastructure.Web.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<ShellDocument> SignIn(HttpClient client, string subjectId)
    {
        var shell = await ReadShell(client);
        var response = await Post(client, "/api/signin", new { subjectId }, shell.RequestToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return await ReadShell(client);
    }

    private static async Task<ShellDocument> ReadShell(HttpClient client)
    {
        var response = await client.GetAsync("/api/shell");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<ShellDocument>(body, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException(body);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<string> ClientBundle(HttpClient client, string html)
    {
        var bundle = "";
        foreach (Match match in AssetPattern.Matches(html))
        {
            var asset = await client.GetAsync(match.Groups[1].Value);
            asset.EnsureSuccessStatusCode();
            bundle += await asset.Content.ReadAsStringAsync();
        }

        Assert.NotEqual(0, bundle.Length);
        return bundle;
    }

    private static string Destination(ShellDocument shell)
    {
        if (!shell.Authenticated || shell.Status == "unauthenticated") return "/signin";
        if (shell.Status == "unavailable") return "/refused?reason=unavailable";
        if (shell.Status == "refused") return "/refused?reason=unmapped";
        if (shell.Workspaces.Length == 0) return "/refused?reason=grant";
        if (shell.Workspaces.Length == 1) return "/workspace";
        if (!string.IsNullOrEmpty(shell.ActiveWorkspace) && shell.Workspaces.Contains(shell.ActiveWorkspace))
            return "/workspace";
        return "/entry";
    }

    private static readonly Regex AssetPattern = new("""(?:src|href)="([^"]+\.(?:js|css))""", RegexOptions.IgnoreCase);

    private sealed record ShellIdentity(string SubjectId, string LegalEntityId, string DisplayName);

    private sealed record ShellDocument(
        bool Authenticated,
        bool LocalDevelopment,
        string Status,
        string? Reason,
        string[] Workspaces,
        string? ActiveWorkspace,
        ShellIdentity[] Identities,
        string RequestToken);

    private async Task<HttpResponseMessage> SendCommand(HttpClient client, string subjectId, string legalEntityId, string workspace)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, legalEntityId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/commands/noop");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "cmd-" + Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }
}
