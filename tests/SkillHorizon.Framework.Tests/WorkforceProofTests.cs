using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SkillHorizon.Workforce;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class WorkforceProofTests
{
    private static readonly string[] Probes =
    [
        "kept-four",
        "moving-report",
        "temp-leaver",
        "only-closed-manager",
        "their-leaver",
        "dup-id"
    ];

    private readonly FrameworkInfrastructure _infrastructure;

    public WorkforceProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Bad_rows_are_quarantined_and_a_leaver_is_closed_without_being_deleted()
    {
        await SyncFixture();
        var nisha = await ReadPerson(FixtureHrFeed.Nisha);
        Assert.NotNull(nisha);
        Assert.Equal("closed", nisha.Status);
        Assert.NotNull(nisha.ClosedAt);
        Assert.Equal("Nisha Iyer", nisha.DisplayName);

        var again = await SyncFixture();
        var retained = await ReadPerson(FixtureHrFeed.Nisha);
        Assert.Equal(1, again.Closed);
        Assert.Equal(nisha.ClosedAt, retained!.ClosedAt);
        Assert.Equal(1, await CountPeople(FixtureHrFeed.Nisha));

        var sync = SyncService();
        var bad = await sync.ApplyAsync(
            [
                new HrRecord("  ", "LE-LOCAL", "IN", null, "active", false, "blank"),
                new HrRecord("dup-id", "LE-LOCAL", "IN", null, "active", false, "One"),
                new HrRecord("dup-id", "LE-LOCAL", "IN", null, "leaver", false, "Two"),
                new HrRecord("person-single", "LE-OTHER", "IN", "person-multi", "leaver", false, "Changed"),
                new HrRecord("person-single", "LE-LOCAL", "IN", null, "active", true, "Also"),
                new HrRecord("kept-id", null, "IN", null, "active", true, "No entity"),
                new HrRecord("kept-country", "LE-LOCAL", " ", null, "active", true, "No country"),
                new HrRecord("kept-status", "LE-LOCAL", "IN", null, "contractor", true, "Bad status"),
                new HrRecord("kept-four", "LE-LOCAL", "IN", null, "active", true, "Kept")
            ],
            CancellationToken.None);

        Assert.Equal(1, bad.Projected);
        Assert.Equal(8, bad.Quarantined);
        Assert.Null(await ReadPerson("dup-id"));
        Assert.Null(await ReadPerson("kept-id"));
        var single = await ReadPerson(FixtureHrFeed.Single);
        Assert.Equal("active", single!.Status);
        Assert.Equal(FixtureHrFeed.Single, single.DisplayName);
        Assert.Null(single.ManagerEmployeeId);
        Assert.True(single.PersonalLearning);
        Assert.Equal(
            ["duplicate_employee_id", "duplicate_employee_id", "duplicate_employee_id", "duplicate_employee_id", "missing_country", "missing_employee_id", "missing_legal_entity", "unrecognized_status"],
            await QuarantineReasons());

        var partial = await sync.ApplyAsync(
            [new HrRecord("temp-leaver", "LE-LOCAL", "IN", FixtureHrFeed.Multi, "leaver", true, "Temp Leaver")],
            CancellationToken.None);
        Assert.Equal(1, partial.Closed);
        var closed = await ReadPerson("temp-leaver");
        Assert.NotNull(closed);
        Assert.Equal("closed", closed.Status);
        Assert.NotNull(closed.ClosedAt);
        var stillThere = await ReadPerson(FixtureHrFeed.Nisha);
        Assert.Equal("closed", stillThere!.Status);

        await sync.ApplyAsync(
            [new HrRecord("temp-leaver", "LE-LOCAL", "IN", FixtureHrFeed.Multi, "leaver", false, "Temp Leaver")],
            CancellationToken.None);
        var closedAgain = await ReadPerson("temp-leaver");
        Assert.Equal(closed.ClosedAt, closedAgain!.ClosedAt);

        await sync.ApplyAsync(
            [new HrRecord("temp-leaver", "LE-LOCAL", "IN", null, "active", false, "Temp Leaver")],
            CancellationToken.None);
        var returned = await ReadPerson("temp-leaver");
        Assert.Equal("active", returned!.Status);
        Assert.Null(returned.ClosedAt);

        await sync.ApplyAsync(
            [new HrRecord("only-closed-manager", "LE-LOCAL", "IN", null, "active", false, "Only Closed Manager")],
            CancellationToken.None);
        await sync.ApplyAsync(
            [
                new HrRecord("only-closed-manager", "LE-LOCAL", "IN", null, "active", false, "Only Closed Manager"),
                new HrRecord("their-leaver", "LE-LOCAL", "IN", "only-closed-manager", "leaver", true, "Their Leaver")
            ],
            CancellationToken.None);
        Assert.Empty(await GrantWorkspaces("only-closed-manager"));
        Assert.Empty(await GrantWorkspaces("their-leaver"));
        Assert.Equal(1, await CountPeople(FixtureHrFeed.Nisha));
    }

    [Fact]
    public async Task Manager_links_are_replaced_and_grants_follow_hr_without_revoking_explicit_ones()
    {
        await SyncFixture();
        await InsertGrant(FixtureHrFeed.Learning, "competency_lead");
        await InsertGrant(FixtureHrFeed.Single, "platform_admin");
        try
        {
            await SyncFixture();
            Assert.Equal(["competency_lead", "learning_and_development"], await GrantWorkspaces(FixtureHrFeed.Learning));
            Assert.Equal(["employee", "platform_admin"], await GrantWorkspaces(FixtureHrFeed.Single));
            Assert.Equal(["employee", "people_manager"], await GrantWorkspaces(FixtureHrFeed.Multi));
            Assert.Equal(["employee"], await GrantWorkspaces(FixtureHrFeed.Asha));
            Assert.Empty(await GrantWorkspaces(FixtureHrFeed.Dev));
            Assert.Empty(await GrantWorkspaces(FixtureHrFeed.Nisha));
            Assert.Empty(await GrantWorkspaces(FixtureHrFeed.NoGrant));
            Assert.Equal(["employee", "people_manager"], await GrantWorkspaces(FixtureHrFeed.OtherManager));

            var sync = SyncService();
            await sync.ApplyAsync(
                [new HrRecord("moving-report", "LE-LOCAL", "IN", FixtureHrFeed.Multi, "active", false, "Moving Report")],
                CancellationToken.None);
            Assert.Contains("moving-report", await ReportIds(FixtureHrFeed.Multi));

            await sync.ApplyAsync(
                [new HrRecord("moving-report", "LE-LOCAL", "IN", FixtureHrFeed.OtherManager, "active", false, "Moving Report")],
                CancellationToken.None);
            var moved = await ReadPerson("moving-report");
            Assert.NotNull(moved);
            Assert.Equal(FixtureHrFeed.OtherManager, moved.ManagerEmployeeId);
            Assert.DoesNotContain("moving-report", await ReportIds(FixtureHrFeed.Multi));
            Assert.Contains("moving-report", await ReportIds(FixtureHrFeed.OtherManager));
            Assert.Contains(FixtureHrFeed.OtherReport, await ReportIds(FixtureHrFeed.OtherManager));
        }
        finally
        {
            await DeleteGrant(FixtureHrFeed.Learning, "competency_lead");
            await DeleteGrant(FixtureHrFeed.Single, "platform_admin");
            await RemoveProbes();
        }
    }

    [Fact]
    public async Task Three_entries_open_from_hr_and_a_manager_sees_only_their_direct_reports()
    {
        await SyncFixture();

        using var single = Client();
        var employee = await SignIn(single, FixtureHrFeed.Single);
        Assert.Equal(["employee"], employee.Workspaces);
        Assert.Equal("employee", employee.ActiveWorkspace);
        Assert.Equal("/workspace", Destination(employee));
        Assert.Equal(HttpStatusCode.Forbidden, (await single.GetAsync("/api/team")).StatusCode);

        using var learning = Client();
        var ld = await SignIn(learning, FixtureHrFeed.Learning);
        Assert.Equal(["learning_and_development"], ld.Workspaces);
        Assert.Equal("learning_and_development", ld.ActiveWorkspace);
        Assert.Equal("/workspace", Destination(ld));
        Assert.DoesNotContain("employee", ld.Workspaces);

        using var several = Client();
        var entry = await SignIn(several, FixtureHrFeed.Multi);
        Assert.Equal(["employee", "people_manager"], entry.Workspaces);
        Assert.Null(entry.ActiveWorkspace);
        Assert.Equal("/entry", Destination(entry));
        Assert.Equal(HttpStatusCode.Forbidden, (await several.GetAsync("/api/team")).StatusCode);

        var deniedLearning = await Post(several, "/api/workspace", new { workspace = "learning_and_development" }, entry.RequestToken);
        Assert.Equal(HttpStatusCode.BadRequest, deniedLearning.StatusCode);
        var deniedAdmin = await Post(several, "/api/workspace", new { workspace = "platform_admin" }, (await ReadShell(several)).RequestToken);
        Assert.Equal(HttpStatusCode.BadRequest, deniedAdmin.StatusCode);

        var chosen = await Post(several, "/api/workspace", new { workspace = "people_manager" }, (await ReadShell(several)).RequestToken);
        Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        var manager = await ReadShell(several);
        Assert.Equal("people_manager", manager.ActiveWorkspace);
        Assert.Equal("/workspace", Destination(manager));

        var teamResponse = await several.GetAsync("/api/team");
        var teamJson = await teamResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, teamResponse.StatusCode);
        Assert.DoesNotContain("score", teamJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("readiness", teamJson, StringComparison.OrdinalIgnoreCase);
        var team = JsonSerializer.Deserialize<TeamDocument>(teamJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException(teamJson);
        Assert.Equal([FixtureHrFeed.Asha, FixtureHrFeed.Dev], team.People.Select(person => person.EmployeeId).ToArray());
        Assert.Equal(["Asha Rao", "Dev Kapoor"], team.People.Select(person => person.DisplayName).ToArray());
        Assert.All(team.People, person =>
        {
            Assert.Equal("LE-LOCAL", person.LegalEntityId);
            Assert.Equal("IN", person.CountryCode);
        });

        var ownTeam = await TeamFromApi(FixtureHrFeed.Multi, "LE-LOCAL");
        Assert.Equal([FixtureHrFeed.Asha, FixtureHrFeed.Dev], ownTeam);
        var otherTeam = await TeamFromApi(FixtureHrFeed.OtherManager, "LE-LOCAL");
        Assert.Equal([FixtureHrFeed.OtherReport], otherTeam);

        using var api = _infrastructure.Api.CreateClient();
        var missingGrant = await SendTeam(api, FixtureHrFeed.Single, "LE-LOCAL", "people_manager");
        Assert.Equal(HttpStatusCode.Forbidden, missingGrant.StatusCode);
        var wrongWorkspace = await SendTeam(api, FixtureHrFeed.Multi, "LE-LOCAL", "employee");
        Assert.Equal(HttpStatusCode.Forbidden, wrongWorkspace.StatusCode);

        using var unmapped = Client();
        var refused = await SignIn(unmapped, "person-unmapped");
        Assert.Equal("refused", refused.Status);
        Assert.Equal("unmapped", refused.Reason);
        Assert.Equal("/refused?reason=unmapped", Destination(refused));

        using var anonymous = Client();
        var signIn = await anonymous.GetAsync("/signin");
        var bundle = await ClientBundle(anonymous, await signIn.Content.ReadAsStringAsync());
        Assert.Contains("Direct reports", bundle, StringComparison.Ordinal);
        Assert.Contains("Your team", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("At risk", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("Needs me", bundle, StringComparison.Ordinal);
    }

    private IWorkforceSync SyncService() =>
        _infrastructure.Api.Services.GetRequiredService<IWorkforceSync>();

    private async Task<WorkforceSyncResult> SyncFixture()
    {
        await RemoveProbes();
        return await SyncService().SyncAsync(CancellationToken.None);
    }

    private async Task RemoveProbes()
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        foreach (var employeeId in Probes)
        {
            await using var relationships = new NpgsqlCommand(
                """
                DELETE FROM workforce.management_relationship
                WHERE manager_employee_id = $1 OR report_employee_id = $1
                """,
                connection);
            relationships.Parameters.AddWithValue(employeeId);
            await relationships.ExecuteNonQueryAsync();

            await using var grants = new NpgsqlCommand(
                "DELETE FROM access.workspace_grant WHERE subject_id = $1",
                connection);
            grants.Parameters.AddWithValue(employeeId);
            await grants.ExecuteNonQueryAsync();

            await using var person = new NpgsqlCommand(
                "DELETE FROM workforce.person WHERE employee_id = $1",
                connection);
            person.Parameters.AddWithValue(employeeId);
            await person.ExecuteNonQueryAsync();
        }
    }

    private async Task<PersonRow?> ReadPerson(string employeeId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT employment_status, closed_at, display_name, manager_employee_id, personal_learning_assigned
            FROM workforce.person
            WHERE employee_id = $1
            """,
            connection);
        command.Parameters.AddWithValue(employeeId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new PersonRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTime>(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4));
    }

    private async Task<int> CountPeople(string employeeId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM workforce.person WHERE employee_id = $1",
            connection);
        command.Parameters.AddWithValue(employeeId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string[]> QuarantineReasons()
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT reason FROM workforce.quarantine ORDER BY reason, employee_id NULLS FIRST",
            connection);
        var reasons = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            reasons.Add(reader.GetString(0));
        return reasons.ToArray();
    }

    private async Task<string[]> GrantWorkspaces(string subjectId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT workspace FROM access.workspace_grant WHERE subject_id = $1 ORDER BY workspace",
            connection);
        command.Parameters.AddWithValue(subjectId);
        var workspaces = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            workspaces.Add(reader.GetString(0));
        return workspaces.ToArray();
    }

    private async Task<string[]> ReportIds(string managerEmployeeId)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT report_employee_id
            FROM workforce.management_relationship
            WHERE manager_employee_id = $1
            ORDER BY report_employee_id
            """,
            connection);
        command.Parameters.AddWithValue(managerEmployeeId);
        var reports = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            reports.Add(reader.GetString(0));
        return reports.ToArray();
    }

    private async Task InsertGrant(string subjectId, string workspace)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
            VALUES (gen_random_uuid(), $1, $2, now())
            ON CONFLICT (subject_id, workspace) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(workspace);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteGrant(string subjectId, string workspace)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM access.workspace_grant WHERE subject_id = $1 AND workspace = $2",
            connection);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(workspace);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> TeamFromApi(string subjectId, string legalEntityId)
    {
        using var client = _infrastructure.Api.CreateClient();
        var response = await SendTeam(client, subjectId, legalEntityId, "people_manager");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var document = JsonSerializer.Deserialize<TeamDocument>(body, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException(body);
        return document.People.Select(person => person.EmployeeId).ToArray();
    }

    private async Task<HttpResponseMessage> SendTeam(HttpClient client, string subjectId, string legalEntityId, string workspace)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, legalEntityId);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/workforce/team");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", "team-" + Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
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

    private sealed record PersonRow(string Status, DateTime? ClosedAt, string DisplayName, string? ManagerEmployeeId, bool PersonalLearning);

    private sealed record TeamPerson(string EmployeeId, string DisplayName, string LegalEntityId, string CountryCode);

    private sealed record TeamDocument(TeamPerson[] People);

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
}
