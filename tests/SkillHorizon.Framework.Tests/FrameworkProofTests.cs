using System.Net;
using System.Net.Http.Headers;
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
        using var unmapped = Client();
        var refused = await Follow(unmapped, await SignIn(unmapped, "person-unmapped"));
        var refusedHtml = await refused.Content.ReadAsStringAsync();
        Assert.Contains("not mapped to a zone", refusedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Choose a workspace", refusedHtml, StringComparison.Ordinal);

        using var several = Client();
        var entry = await Follow(several, await SignIn(several, "person-multi"));
        var entryHtml = await entry.Content.ReadAsStringAsync();
        Assert.Contains("Choose a workspace", entryHtml, StringComparison.Ordinal);
        Assert.Contains("Employee", entryHtml, StringComparison.Ordinal);
        Assert.Contains("People manager", entryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Platform admin", entryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Competency lead", entryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("L&amp;D", entryHtml, StringComparison.Ordinal);

        var token = InputValue(entryHtml, "__RequestVerificationToken");
        using var choose = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["workspace"] = "people_manager",
            ["__RequestVerificationToken"] = token
        });
        var chosen = await Follow(several, await several.PostAsync("/entry", choose));
        var shell = await chosen.Content.ReadAsStringAsync();
        Assert.Contains("People manager", shell, StringComparison.Ordinal);
        Assert.Contains("Active workspace", shell, StringComparison.Ordinal);
        Assert.Contains("Choose a different workspace", shell, StringComparison.Ordinal);

        using var single = Client();
        var direct = await Follow(single, await SignIn(single, "person-single"));
        var directHtml = await direct.Content.ReadAsStringAsync();
        Assert.Contains("Active workspace", directHtml, StringComparison.Ordinal);
        Assert.Contains("Employee", directHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Choose a workspace", directHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("People manager", directHtml, StringComparison.Ordinal);
    }

    private HttpClient Client() =>
        _infrastructure.Web.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<HttpResponseMessage> SignIn(HttpClient client, string subjectId)
    {
        var page = await client.GetAsync("/signin");
        var html = await page.Content.ReadAsStringAsync();
        var token = InputValue(html, "__RequestVerificationToken");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["subjectId"] = subjectId,
            ["__RequestVerificationToken"] = token
        });
        return await client.PostAsync("/signin", form);
    }

    private static async Task<HttpResponseMessage> Follow(HttpClient client, HttpResponseMessage response)
    {
        for (var hop = 0; hop < 6 && (int)response.StatusCode is >= 300 and < 400; hop++)
        {
            var location = response.Headers.Location ?? throw new InvalidOperationException("Missing redirect.");
            var uri = location.IsAbsoluteUri ? location : new Uri(client.BaseAddress!, location);
            response = await client.GetAsync(uri);
        }

        return response;
    }

    private static string InputValue(string html, string name)
    {
        var marker = $"name=\"{name}\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Input {name} was not on the page. {html}");
        start += marker.Length;
        var end = html.IndexOf('"', start);
        return WebUtility.HtmlDecode(html[start..end]);
    }

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
