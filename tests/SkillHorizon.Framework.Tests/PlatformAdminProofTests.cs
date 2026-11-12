using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SkillHorizon.Framework.Tests;

[Collection(FrameworkCollection.Name)]
public sealed class PlatformAdminProofTests
{
    private readonly FrameworkInfrastructure _infrastructure;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public PlatformAdminProofTests(FrameworkInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Platform_admin_runs_access_security_audit_and_exports_without_responses()
    {
        var target = "admin-target-" + _suffix;
        var person = "export-person-" + _suffix;
        var responseBody = "response-body-" + _suffix;
        var evidence = "evidence-bytes-" + _suffix;
        var secret = "key-material-" + _suffix;
        var otherZone = "other-" + _suffix;
        var otherConnector = "other-connector-" + _suffix;
        var sittingId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var correlation = "admin-" + _suffix;
        var sittingsBefore = await AssessmentCount("SELECT count(*) FROM assessment.sitting");
        try
        {
            using var api = _infrastructure.Api.CreateClient();
            foreach (var (method, path, body) in AdminRoutes())
            {
                var refused = await Send(api, method, path, "person-single", "employee", body, correlation);
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            }

            var granted = await Send(api, HttpMethod.Post, "/commands/admin/grants", "person-admin", "platform_admin", new
            {
                subjectId = target,
                workspace = "employee",
                sittingId
            }, correlation);
            var grantedBody = await granted.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            Assert.DoesNotContain(sittingId.ToString(), grantedBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", grantedBody, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(sittingsBefore, await AssessmentCount("SELECT count(*) FROM assessment.sitting"));
            Assert.Equal(1, await DomainCount("SELECT count(*) FROM access.workspace_grant WHERE subject_id = $1 AND workspace = 'employee'", target));
            Assert.Equal(1, await DomainCount("SELECT count(*) FROM audit.audit_record WHERE action = 'grant.issued' AND correlation_id = $1 AND detail::text LIKE '%' || $2 || '%'", correlation, target));

            var again = await Send(api, HttpMethod.Post, "/commands/admin/grants", "person-admin", "platform_admin", new { subjectId = target, workspace = "employee" }, correlation);
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
            Assert.Equal(1, await DomainCount("SELECT count(*) FROM audit.audit_record WHERE action = 'grant.issued' AND correlation_id = $1 AND detail::text LIKE '%' || $2 || '%'", correlation, target));

            var opened = await Send(api, HttpMethod.Post, "/commands/sittings/authorize", "person-admin", "platform_admin", new { waveId = Guid.NewGuid() }, correlation);
            Assert.Equal(HttpStatusCode.Forbidden, opened.StatusCode);
            Assert.Equal(sittingsBefore, await AssessmentCount("SELECT count(*) FROM assessment.sitting"));

            var revoked = await Send(api, HttpMethod.Post, "/commands/admin/grants/revoke", "person-admin", "platform_admin", new { subjectId = target, workspace = "employee" }, correlation);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            Assert.Equal(0, await DomainCount("SELECT count(*) FROM access.workspace_grant WHERE subject_id = $1 AND workspace = 'employee'", target));
            Assert.Equal(1, await DomainCount("SELECT count(*) FROM audit.audit_record WHERE action = 'grant.revoked' AND correlation_id = $1 AND detail::text LIKE '%' || $2 || '%'", correlation, target));

            await Control(
                """
                UPDATE integration.connector
                SET secret_name = $1, endpoint = 'https://hr.example/feed'
                WHERE connector_id = 'local-hr'
                """,
                secret);
            await Control(
                """
                INSERT INTO zone.zone (zone_id, display_name, status, api_base_url, registered_at)
                VALUES ($1, 'Other zone', 'provisioning', 'http://other', now())
                """,
                otherZone);
            await Control(
                """
                INSERT INTO integration.connector (connector_id, zone_id, connector_type, endpoint, secret_name, enabled)
                VALUES ($1, $2, 'calendar', 'https://other.example', $3, true)
                """,
                otherConnector,
                otherZone,
                secret);

            var security = await Send(api, HttpMethod.Get, "/admin/security", "person-admin", "platform_admin", null, correlation);
            var securityBody = await security.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, security.StatusCode);
            Assert.Contains("Local zone", securityBody, StringComparison.Ordinal);
            Assert.Contains("LE-LOCAL", securityBody, StringComparison.Ordinal);
            Assert.Contains("https://hr.example/feed", securityBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, securityBody, StringComparison.Ordinal);
            Assert.DoesNotContain(otherConnector, securityBody, StringComparison.Ordinal);
            Assert.DoesNotContain("response", securityBody, StringComparison.OrdinalIgnoreCase);
            var keys = await Send(api, HttpMethod.Post, "/admin/security", "person-admin", "platform_admin", new { }, correlation);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, keys.StatusCode);
            var missing = await Send(api, HttpMethod.Post, "/admin/keys", "person-admin", "platform_admin", new { }, correlation);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            var integrations = await Send(api, HttpMethod.Get, "/admin/integrations", "person-admin", "platform_admin", null, correlation);
            var integrationsBody = await integrations.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, integrations.StatusCode);
            Assert.Contains("hr", integrationsBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, integrationsBody, StringComparison.Ordinal);
            Assert.DoesNotContain(otherConnector, integrationsBody, StringComparison.Ordinal);

            var models = await Send(api, HttpMethod.Get, "/admin/models", "person-admin", "platform_admin", null, correlation);
            var modelsBody = await models.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, models.StatusCode);
            Assert.Equal(0, JsonSerializer.Deserialize<JsonElement>(modelsBody).GetProperty("models").GetArrayLength());
            Assert.Equal(0, await DomainCount("SELECT count(*) FROM audit.model_register WHERE connected"));

            await Execute(
                """
                INSERT INTO audit.audit_record (
                    audit_id, subject_id, action, record_type, record_id, correlation_id, detail, occurred_at)
                VALUES ($1, 'person-admin', 'probe.read', 'audit.audit_record', $2, $3, $4::jsonb, now())
                """,
                Guid.NewGuid(),
                Guid.NewGuid(),
                correlation,
                "{\"response\":\"" + responseBody + "\",\"evidence\":\"" + evidence + "\"}");
            var audit = await Send(api, HttpMethod.Get, "/admin/audit", "person-admin", "platform_admin", null, correlation);
            var auditBody = await audit.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
            Assert.Contains("audit.listed", auditBody, StringComparison.Ordinal);
            Assert.Contains("probe.read", auditBody, StringComparison.Ordinal);
            Assert.DoesNotContain(responseBody, auditBody, StringComparison.Ordinal);
            Assert.DoesNotContain(evidence, auditBody, StringComparison.Ordinal);
            var listedDetail = await DomainText("SELECT detail::text FROM audit.audit_record WHERE action = 'audit.listed' AND correlation_id = $1 ORDER BY occurred_at DESC LIMIT 1", correlation);
            Assert.Contains("listed", listedDetail, StringComparison.Ordinal);
            Assert.DoesNotContain(responseBody, listedDetail, StringComparison.Ordinal);
            Assert.DoesNotContain(evidence, listedDetail, StringComparison.Ordinal);
            Assert.Equal(0, await DomainCount("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'audit' AND column_name IN ('response_body', 'evidence_bytes')"));

            await PlantResponse(sittingId, itemId, responseBody);
            var openedCase = await Send(api, HttpMethod.Post, "/commands/admin/malpractice", "person-admin", "platform_admin", new { sittingId, status = "open" }, correlation);
            var caseBody = await openedCase.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, openedCase.StatusCode);
            Assert.Contains(sittingId.ToString(), caseBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(responseBody, caseBody, StringComparison.Ordinal);
            Assert.Equal(0, await DomainCount("SELECT count(*) FROM audit.malpractice_case WHERE sitting_id = $1 AND (status || opened_by) LIKE '%' || $2 || '%'", sittingId, responseBody));
            Assert.True(await AssessmentCount("SELECT count(*) FROM assessment.mark WHERE response_body = $1", responseBody) > 0);

            var types = await Send(api, HttpMethod.Get, "/admin/exports", "person-admin", "platform_admin", null, correlation);
            var typeBody = await types.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, types.StatusCode);
            var codes = JsonSerializer.Deserialize<JsonElement>(typeBody).GetProperty("types").EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToArray();
            Assert.Contains("personal_metadata", codes);
            Assert.DoesNotContain(codes, code => code!.Contains("response", StringComparison.OrdinalIgnoreCase));

            var missingPerson = await Send(api, HttpMethod.Post, "/commands/admin/exports/personal", "person-admin", "platform_admin", new { subjectId = person }, correlation);
            Assert.Equal(HttpStatusCode.BadRequest, missingPerson.StatusCode);
            Assert.Equal(0, await DomainCount("SELECT count(*) FROM audit.personal_export WHERE subject_id = $1", person));

            await Execute(
                """
                INSERT INTO workforce.person (
                    employee_id, legal_entity_id, country_code, manager_employee_id, employment_status,
                    personal_learning_assigned, display_name, projected_at)
                VALUES ($1, 'LE-LOCAL', 'IN', NULL, 'active', false, $2, now())
                """,
                person,
                "Export " + _suffix);
            var exported = await Send(api, HttpMethod.Post, "/commands/admin/exports/personal", "person-admin", "platform_admin", new { subjectId = person }, correlation);
            var exportBody = await exported.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
            Assert.Contains("Export " + _suffix, exportBody, StringComparison.Ordinal);
            Assert.Contains("LE-LOCAL", exportBody, StringComparison.Ordinal);
            Assert.DoesNotContain(responseBody, exportBody, StringComparison.Ordinal);
            Assert.Equal(1, await DomainCount("SELECT count(*) FROM audit.personal_export WHERE subject_id = $1", person));
            Assert.Equal(1, await DomainCount("SELECT count(*) FROM audit.audit_record WHERE action = 'export.personal_metadata' AND correlation_id = $1 AND detail::text LIKE '%' || $2 || '%'", correlation, person));
            var exportDetail = await DomainText("SELECT detail::text FROM audit.audit_record WHERE action = 'export.personal_metadata' AND correlation_id = $1", correlation);
            Assert.DoesNotContain(responseBody, exportDetail, StringComparison.Ordinal);

            using var web = WebClient();
            await SignIn(web, "person-admin");
            await Read(web, "/api/shell");
            var shellGrants = await Read(web, "/api/admin/grants");
            Assert.Contains("person-admin", shellGrants.GetRawText(), StringComparison.Ordinal);
            using var employeeWeb = WebClient();
            await SignIn(employeeWeb, "person-single");
            await Read(employeeWeb, "/api/shell");
            foreach (var path in new[] { "/api/admin/grants", "/api/admin/security", "/api/admin/models", "/api/admin/audit", "/api/admin/malpractice", "/api/admin/integrations", "/api/admin/exports" })
            {
                var response = await employeeWeb.GetAsync(path);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        finally
        {
            await Assessment("DELETE FROM assessment.mark WHERE sitting_id = $1", sittingId);
            await Assessment("DELETE FROM assessment.sitting WHERE sitting_id = $1", sittingId);
            await Assessment("DELETE FROM assessment.item WHERE item_id = $1", itemId);
            await Execute("DELETE FROM audit.malpractice_case WHERE sitting_id = $1", sittingId);
            await Execute("DELETE FROM audit.personal_export WHERE subject_id = $1", person);
            await Execute("DELETE FROM workforce.person WHERE employee_id = $1", person);
            await Execute("DELETE FROM access.workspace_grant WHERE subject_id = $1", target);
            await Execute("DELETE FROM audit.audit_record WHERE correlation_id = $1", correlation);
            await Control("UPDATE integration.connector SET secret_name = NULL, endpoint = NULL WHERE connector_id = 'local-hr'");
            await Control("DELETE FROM integration.connector WHERE connector_id = $1", otherConnector);
            await Control("DELETE FROM zone.zone WHERE zone_id = $1", otherZone);
        }
    }

    private IEnumerable<(HttpMethod Method, string Path, object? Body)> AdminRoutes()
    {
        yield return (HttpMethod.Get, "/admin/grants", null);
        yield return (HttpMethod.Post, "/commands/admin/grants", new { subjectId = "x", workspace = "employee" });
        yield return (HttpMethod.Post, "/commands/admin/grants/revoke", new { subjectId = "x", workspace = "employee" });
        yield return (HttpMethod.Get, "/admin/security", null);
        yield return (HttpMethod.Get, "/admin/models", null);
        yield return (HttpMethod.Get, "/admin/audit", null);
        yield return (HttpMethod.Get, "/admin/malpractice", null);
        yield return (HttpMethod.Post, "/commands/admin/malpractice", new { sittingId = Guid.NewGuid(), status = "open" });
        yield return (HttpMethod.Get, "/admin/integrations", null);
        yield return (HttpMethod.Get, "/admin/exports", null);
        yield return (HttpMethod.Post, "/commands/admin/exports/personal", new { subjectId = "x" });
    }

    private async Task PlantResponse(Guid sittingId, Guid itemId, string responseBody)
    {
        await Assessment(
            """
            INSERT INTO assessment.item (item_id, skill_code, prompt)
            VALUES ($1, $2, $2)
            """,
            itemId,
            "skill-" + _suffix);
        await Assessment(
            """
            INSERT INTO assessment.sitting (
                sitting_id, authorization_id, token_hash, subject_id, wave_id, benchmark_version_id, scale, status, opened_at)
            VALUES ($1, $2, 'hash', 'person-single', $2, $2, '[]'::jsonb, 'open', now())
            """,
            sittingId,
            Guid.NewGuid());
        await Assessment(
            """
            INSERT INTO assessment.mark (sitting_id, item_id, response_body, band, marked_at)
            VALUES ($1, $2, $3, 1, now())
            """,
            sittingId,
            itemId,
            responseBody);
    }

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string subjectId, string workspace, object? body, string correlation)
    {
        var token = await _infrastructure.IssueTokenAsync(subjectId, "LE-LOCAL");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Active-Workspace", workspace);
        request.Headers.Add("X-Correlation-Id", correlation);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private HttpClient WebClient() =>
        _infrastructure.Web.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task SignIn(HttpClient client, string subjectId)
    {
        var shell = await Read(client, "/api/shell");
        var response = await Post(client, "/api/signin", new { subjectId }, shell.GetProperty("requestToken").GetString()!);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private Task Execute(string sql, params object[] values) => Run(_infrastructure.DomainConnection, sql, values);

    private Task Control(string sql, params object[] values) => Run(_infrastructure.ControlConnection, sql, values);

    private Task Assessment(string sql, params object[] values) => Run(_infrastructure.AssessmentConnection, sql, values);

    private static async Task Run(string connectionString, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> DomainCount(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string> DomainText(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.DomainConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> AssessmentCount(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(_infrastructure.AssessmentConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
