using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using SkillHorizon.Hosting;

namespace SkillHorizon.Web;

public static class ShellEndpoints
{
    public static WebApplication MapSkillHorizonShell(this WebApplication app)
    {
        app.MapGet("/api/shell", async (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await Describe(http).ConfigureAwait(false));
        });
        app.MapPost("/api/signin", (Delegate)SignIn);
        app.MapPost("/api/signout", (Delegate)SignOut);
        app.MapPost("/api/workspace", (Delegate)Choose);
        app.MapGet("/api/team", (Delegate)Team);
        app.MapGet("/api/standards", (Delegate)Standards);
        app.MapGet("/api/assessment", (Delegate)Assessment);
        app.MapPost("/api/sittings/start", (Delegate)StartSitting);
        app.MapPost("/api/sittings/answer", (Delegate)AnswerSitting);
        app.MapPost("/api/sittings/seal", (Delegate)SealSitting);
        app.MapPost("/api/progress/steps", (Delegate)AssignStep);
        app.MapPost("/api/paths", (Delegate)OpenPath);
        app.MapGet("/api/path", (Delegate)ReadPath);
        app.MapPost("/api/paths/evidence", (Delegate)AddEvidence);
        app.MapGet("/api/team/paths", (Delegate)TeamPaths);
        app.MapPost("/api/validations", (Delegate)ValidateStep);
        app.MapPost("/api/benchmarks/draft", (Delegate)SaveBenchmark);
        app.MapPost("/api/benchmarks/publish", (Delegate)PublishBenchmark);
        app.MapPost("/api/packs", (Delegate)SubmitPack);
        app.MapGet("/api/packs", (Delegate)ListPacks);
        app.MapPost("/api/packs/{packId:guid}/seal", (Delegate)SealPack);
        app.MapPost("/api/cohorts", (Delegate)CreateCohort);
        app.MapGet("/api/practice/readiness", (Delegate)PracticeReadiness);
        app.MapGet("/signin/oidc", (Delegate)Oidc);
        app.MapFallback(ServeClient);
        return app;
    }

    private static async Task ServeClient(HttpContext http, IWebHostEnvironment environment)
    {
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var path = http.Request.Path.Value ?? "";
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) || Path.HasExtension(path))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var root = environment.WebRootPath;
        var index = string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "index.html");
        if (index.Length == 0 || !File.Exists(index))
        {
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            http.Response.ContentType = "text/plain; charset=utf-8";
            await http.Response.WriteAsync("The web client has not been built.").ConfigureAwait(false);
            return;
        }

        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.Headers.CacheControl = "no-cache";
        await http.Response.SendFileAsync(index, http.RequestAborted).ConfigureAwait(false);
    }

    private static IResult Oidc(HttpContext http)
    {
        var identity = http.RequestServices.GetRequiredService<IIdentityProvider>();
        if (identity.IsLocalDevelopment || http.User.Identity?.IsAuthenticated == true)
            return Results.Redirect(http.User.Identity?.IsAuthenticated == true ? "/" : "/signin");

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> SignIn(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });

        var identity = http.RequestServices.GetRequiredService<IIdentityProvider>();
        if (!identity.IsLocalDevelopment)
            return Results.BadRequest(new { error = "Sign in with the organisation identity provider." });

        SignInRequest? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<SignInRequest>(http.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "That identity is not in the local development directory." });
        }

        var match = identity.FindDevelopmentIdentity(body?.SubjectId?.Trim() ?? "");
        if (match is null)
            return Results.BadRequest(new { error = "That identity is not in the local development directory." });

        var claims = new List<Claim>
        {
            new("sub", match.SubjectId),
            new("employee_id", match.SubjectId),
            new("legal_entity", match.LegalEntityId),
            new("display_name", match.DisplayName),
            new(ClaimTypes.Name, match.DisplayName)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> SignOut(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> Choose(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        WorkspaceRequest? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<WorkspaceRequest>(http.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "That workspace is not held." });
        }

        var workspace = body?.Workspace?.Trim() ?? "";
        var lookup = await Lookup(http).ConfigureAwait(false);
        if (lookup.Status != WorkspaceLookupStatus.Ok || !lookup.Workspaces.Contains(workspace, StringComparer.Ordinal))
            return Results.BadRequest(new { error = "That workspace is not held." });

        await RememberWorkspace(http, workspace).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> Team(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        var active = http.User.FindFirst("active_workspace")?.Value;
        if (!string.Equals(active, "people_manager", StringComparison.Ordinal))
            return Results.Json(new { error = "Direct reports are read from the people manager workspace." }, statusCode: StatusCodes.Status403Forbidden);

        var assertion = Assertion(http.User);
        if (assertion is null)
            return Results.Unauthorized();

        var bearer = await Bearer(http, assertion).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(bearer))
            return Results.Unauthorized();

        var api = http.RequestServices.GetRequiredService<CoreApiClient>();
        var team = await api.GetTeamAsync(bearer, Correlation(http) ?? "", http.RequestAborted).ConfigureAwait(false);
        return team.Status switch
        {
            TeamLookupStatus.Ok => Results.Ok(new { people = team.People }),
            TeamLookupStatus.Unauthenticated => Results.Unauthorized(),
            TeamLookupStatus.Unavailable => Results.Json(new { error = "The team list could not be loaded." }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(new { error = "Direct reports are read from the people manager workspace." }, statusCode: StatusCodes.Status403Forbidden)
        };
    }

    private static Task<IResult> Standards(HttpContext http) =>
        Proxy(http, HttpMethod.Get, "standards", null);

    private static async Task<IResult> SaveBenchmark(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var body = await ReadBody(http).ConfigureAwait(false);
        return await Proxy(http, HttpMethod.Post, "commands/benchmarks/draft", body).ConfigureAwait(false);
    }

    private static Task<IResult> Assessment(HttpContext http) =>
        EmployeeProxy(http, HttpMethod.Get, "assessment", null);

    private static async Task<IResult> StartSitting(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;

        var body = await ReadBody(http).ConfigureAwait(false);
        var reply = await Core(http, HttpMethod.Post, "commands/sittings/authorize", body).ConfigureAwait(false);
        if (reply.Status != StatusCodes.Status200OK)
            return Results.Text(reply.Body, "application/json", statusCode: reply.Status);

        string? token;
        try
        {
            using var authorized = JsonDocument.Parse(reply.Body);
            token = authorized.RootElement.GetProperty("token").GetString();
        }
        catch (JsonException)
        {
            return Results.Problem(title: "Sitting refused", detail: "The sitting could not be opened.", statusCode: StatusCodes.Status502BadGateway);
        }

        var opened = await Learning(http, HttpMethod.Post, "sittings", token, null).ConfigureAwait(false);
        if (opened.Status != StatusCodes.Status200OK)
            return Results.Text(opened.Body, "application/json", statusCode: opened.Status);

        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(opened.Body)?.AsObject()
                ?? throw new JsonException();
            node["token"] = token;
            return Results.Text(node.ToJsonString(), "application/json");
        }
        catch (JsonException)
        {
            return Results.Problem(title: "Sitting refused", detail: "The sitting could not be opened.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> AnswerSitting(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return await ForwardSitting(http, "responses").ConfigureAwait(false);
    }

    private static async Task<IResult> SealSitting(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return await ForwardSitting(http, "seal").ConfigureAwait(false);
    }

    private static async Task<IResult> OpenPath(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var session = await Session(http).ConfigureAwait(false);
        return await LearningUser(http, HttpMethod.Post, "paths", session!.Value.Bearer, "employee", "{}").ConfigureAwait(false);
    }

    private static async Task<IResult> ReadPath(HttpContext http)
    {
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var session = await Session(http).ConfigureAwait(false);
        return await LearningUser(http, HttpMethod.Get, "paths/current", session!.Value.Bearer, "employee", null).ConfigureAwait(false);
    }

    private static async Task<IResult> AddEvidence(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var session = await Session(http).ConfigureAwait(false);
        var body = await ReadBody(http).ConfigureAwait(false);
        return await LearningUser(http, HttpMethod.Post, "paths/evidence", session!.Value.Bearer, "employee", body).ConfigureAwait(false);
    }

    private static async Task<IResult> TeamPaths(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        if (!string.Equals(http.User.FindFirst("active_workspace")?.Value, "people_manager", StringComparison.Ordinal))
            return Results.Json(new { error = "Choose the people manager workspace first." }, statusCode: StatusCodes.Status403Forbidden);
        var session = await Session(http).ConfigureAwait(false);
        if (session is null)
            return Results.Unauthorized();
        return await LearningUser(http, HttpMethod.Get, "team/paths", session.Value.Bearer, "people_manager", null).ConfigureAwait(false);
    }

    private static async Task<IResult> ValidateStep(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        if (!string.Equals(http.User.FindFirst("active_workspace")?.Value, "people_manager", StringComparison.Ordinal))
            return Results.Json(new { error = "Choose the people manager workspace first." }, statusCode: StatusCodes.Status403Forbidden);
        var session = await Session(http).ConfigureAwait(false);
        if (session is null)
            return Results.Unauthorized();
        var body = await ReadBody(http).ConfigureAwait(false);
        return await LearningUser(http, HttpMethod.Post, "validations", session.Value.Bearer, "people_manager", body).ConfigureAwait(false);
    }

    private static async Task<IResult> LearningUser(HttpContext http, HttpMethod method, string path, string bearer, string workspace, string? json)
    {
        var learning = http.RequestServices.GetRequiredService<LearningApiClient>();
        var reply = await learning.SendAsync(method, path, null, bearer, workspace, Correlation(http), json, http.RequestAborted).ConfigureAwait(false);
        return Results.Text(reply.Body, "application/json", statusCode: reply.Status);
    }

    private static async Task<IResult> AssignStep(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;

        var body = await ReadBody(http).ConfigureAwait(false);
        var session = await Session(http).ConfigureAwait(false);
        if (session is null)
            return Results.Unauthorized();
        var learning = http.RequestServices.GetRequiredService<LearningApiClient>();
        var reply = await learning.SendAsync(
            HttpMethod.Post,
            "progress/steps",
            null,
            session.Value.Bearer,
            "employee",
            Correlation(http),
            body,
            http.RequestAborted).ConfigureAwait(false);
        return Results.Text(reply.Body, "application/json", statusCode: reply.Status);
    }

    private static async Task<IResult> ForwardSitting(HttpContext http, string action)
    {
        var body = await ReadBody(http).ConfigureAwait(false) ?? "{}";
        string token;
        Guid sittingId;
        try
        {
            using var document = JsonDocument.Parse(body);
            token = document.RootElement.GetProperty("token").GetString() ?? "";
            sittingId = document.RootElement.GetProperty("sittingId").GetGuid();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            return Results.BadRequest(new { error = "The sitting could not be continued." });
        }

        var reply = await Learning(http, HttpMethod.Post, $"sittings/{sittingId}/{action}", token, action == "seal" ? null : body).ConfigureAwait(false);
        return Results.Text(reply.Body, "application/json", statusCode: reply.Status);
    }

    private static async Task<IResult> EmployeeProxy(HttpContext http, HttpMethod method, string path, string? json)
    {
        var refused = await EmployeeContext(http).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var reply = await Core(http, method, path, json).ConfigureAwait(false);
        return Results.Text(reply.Body, "application/json", statusCode: reply.Status);
    }

    private static async Task<IResult?> EmployeeContext(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        var active = http.User.FindFirst("active_workspace")?.Value;
        if (!string.Equals(active, "employee", StringComparison.Ordinal))
            return Results.Json(new { error = "Choose the employee workspace first." }, statusCode: StatusCodes.Status403Forbidden);
        return await Session(http).ConfigureAwait(false) is null ? Results.Unauthorized() : null;
    }

    private static async Task<ApiReply> Core(HttpContext http, HttpMethod method, string path, string? json)
    {
        var session = await Session(http).ConfigureAwait(false);
        var api = http.RequestServices.GetRequiredService<CoreApiClient>();
        return await api.SendAsync(method, path, "employee", session!.Value.Bearer, Correlation(http) ?? "", json, http.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<ApiReply> Learning(HttpContext http, HttpMethod method, string path, string? token, string? json)
    {
        var learning = http.RequestServices.GetRequiredService<LearningApiClient>();
        return await learning.SendAsync(method, path, token, null, null, Correlation(http), json, http.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<(string Bearer, IdentityAssertion Assertion)?> Session(HttpContext http)
    {
        var assertion = Assertion(http.User);
        if (assertion is null)
            return null;
        var bearer = await Bearer(http, assertion).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(bearer) ? null : (bearer, assertion);
    }

    private static async Task<IResult> SubmitPack(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var body = await ReadBody(http).ConfigureAwait(false);
        return await Proxy(http, HttpMethod.Post, "commands/packs/submit", body).ConfigureAwait(false);
    }

    private static async Task<IResult> ListPacks(HttpContext http)
    {
        var practice = http.Request.Query["practice"].ToString();
        var path = string.IsNullOrWhiteSpace(practice) ? "packs" : "packs?practice=" + Uri.EscapeDataString(practice);
        return await Proxy(http, HttpMethod.Get, path, null).ConfigureAwait(false);
    }

    private static async Task<IResult> SealPack(HttpContext http, Guid packId)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        return await Proxy(http, HttpMethod.Post, $"commands/packs/{packId}/seal", "{}").ConfigureAwait(false);
    }

    private static async Task<IResult> CreateCohort(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var body = await ReadBody(http).ConfigureAwait(false);
        return await Proxy(http, HttpMethod.Post, "commands/cohorts", body).ConfigureAwait(false);
    }

    private static Task<IResult> PracticeReadiness(HttpContext http)
    {
        var practice = Uri.EscapeDataString(http.Request.Query["practice"].ToString());
        return Proxy(http, HttpMethod.Get, "practice/readiness?practice=" + practice, null);
    }

    private static async Task<IResult> PublishBenchmark(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest(new { error = "The form expired. Reload the page and try again." });
        var body = await ReadBody(http).ConfigureAwait(false);
        return await Proxy(http, HttpMethod.Post, "commands/benchmarks/publish", body).ConfigureAwait(false);
    }

    private static async Task<string?> ReadBody(HttpContext http)
    {
        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(http.RequestAborted).ConfigureAwait(false);
        return body.Length == 0 ? "{}" : body;
    }

    private static async Task<IResult> Proxy(HttpContext http, HttpMethod method, string path, string? json)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        var active = http.User.FindFirst("active_workspace")?.Value;
        if (string.IsNullOrWhiteSpace(active))
            return Results.Json(new { error = "Choose a workspace first." }, statusCode: StatusCodes.Status403Forbidden);

        var assertion = Assertion(http.User);
        if (assertion is null)
            return Results.Unauthorized();
        var bearer = await Bearer(http, assertion).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(bearer))
            return Results.Unauthorized();

        var api = http.RequestServices.GetRequiredService<CoreApiClient>();
        var reply = await api.SendAsync(method, path, active, bearer, Correlation(http) ?? "", json, http.RequestAborted).ConfigureAwait(false);
        return Results.Text(reply.Body, "application/json", statusCode: reply.Status);
    }

    private static async Task<ShellState> Describe(HttpContext http)
    {
        var identityProvider = http.RequestServices.GetRequiredService<IIdentityProvider>();
        var correlationId = Correlation(http);
        var requestToken = Token(http);
        var authenticated = http.User.Identity?.IsAuthenticated == true;
        if (!authenticated)
        {
            var identities = identityProvider.IsLocalDevelopment
                ? identityProvider.DevelopmentIdentities
                    .Select(identity => new ShellIdentity(identity.SubjectId, identity.LegalEntityId, identity.DisplayName))
                    .ToArray()
                : [];
            return new ShellState(false, identityProvider.IsLocalDevelopment, null, null, null, "unauthenticated", null, [], null, identities, correlationId, requestToken);
        }

        var subject = http.User.FindFirst("sub")?.Value;
        var displayName = http.User.FindFirst("display_name")?.Value
            ?? http.User.FindFirst(ClaimTypes.Name)?.Value
            ?? subject;
        var legalEntity = http.User.FindFirst("legal_entity")?.Value;
        var lookup = await Lookup(http).ConfigureAwait(false);
        if (lookup.Status == WorkspaceLookupStatus.Unauthenticated)
            return new ShellState(true, identityProvider.IsLocalDevelopment, subject, displayName, legalEntity, "unauthenticated", null, [], null, [], correlationId, requestToken);
        if (lookup.Status == WorkspaceLookupStatus.Unavailable)
            return new ShellState(true, identityProvider.IsLocalDevelopment, subject, displayName, legalEntity, "unavailable", null, [], null, [], correlationId, requestToken);
        if (lookup.Status == WorkspaceLookupStatus.Refused)
            return new ShellState(true, identityProvider.IsLocalDevelopment, subject, displayName, legalEntity, "refused", "unmapped", [], null, [], correlationId, requestToken);
        if (lookup.Workspaces.Count == 0)
            return new ShellState(true, identityProvider.IsLocalDevelopment, subject, displayName, legalEntity, "ok", "grant", [], null, [], correlationId, requestToken);

        var active = await AlignWorkspace(http, lookup.Workspaces, http.User.FindFirst("active_workspace")?.Value).ConfigureAwait(false);
        return new ShellState(true, identityProvider.IsLocalDevelopment, subject, displayName, legalEntity, "ok", null, lookup.Workspaces, active, [], correlationId, requestToken);
    }

    private static async Task<string?> AlignWorkspace(HttpContext http, IReadOnlyList<string> workspaces, string? active)
    {
        var next = workspaces.Count == 1
            ? workspaces[0]
            : workspaces.Contains(active ?? "", StringComparer.Ordinal) ? active : null;
        if (!string.Equals(next, string.IsNullOrWhiteSpace(active) ? null : active, StringComparison.Ordinal))
            await RememberWorkspace(http, next).ConfigureAwait(false);
        return next;
    }

    private static async Task<WorkspaceLookup> Lookup(HttpContext http)
    {
        var assertion = Assertion(http.User);
        if (assertion is null)
            return WorkspaceLookup.Unauthenticated();

        var bearer = await Bearer(http, assertion).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(bearer))
            return WorkspaceLookup.Unauthenticated();

        var api = http.RequestServices.GetRequiredService<CoreApiClient>();
        return await api.GetWorkspacesAsync(bearer, Correlation(http) ?? "", http.RequestAborted).ConfigureAwait(false);
    }

    private static IdentityAssertion? Assertion(ClaimsPrincipal user)
    {
        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var employee = user.FindFirst("employee_id")?.Value;
        var legalEntity = user.FindFirst("legal_entity")?.Value ?? "";
        return new IdentityAssertion(subject, string.IsNullOrWhiteSpace(employee) ? subject : employee, legalEntity);
    }

    private static async Task<string?> Bearer(HttpContext http, IdentityAssertion assertion)
    {
        var identity = http.RequestServices.GetRequiredService<IIdentityProvider>();
        if (identity.IsLocalDevelopment)
            return await identity.IssueTokenAsync(assertion, http.RequestAborted).ConfigureAwait(false);

        return await http.GetTokenAsync("access_token").ConfigureAwait(false);
    }

    private static async Task RememberWorkspace(HttpContext http, string? workspace)
    {
        var identity = (ClaimsIdentity)http.User.Identity!;
        var clone = identity.Clone();
        foreach (var claim in clone.FindAll("active_workspace").ToList())
            clone.RemoveClaim(claim);
        if (!string.IsNullOrWhiteSpace(workspace))
            clone.AddClaim(new Claim("active_workspace", workspace));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(clone))
            .ConfigureAwait(false);
    }

    private static string Token(HttpContext http)
    {
        var tokens = http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
        return tokens.RequestToken ?? "";
    }

    private static async Task<bool> IsValidForm(HttpContext http)
    {
        try
        {
            await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http).ConfigureAwait(false);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static string? Correlation(HttpContext http) =>
        http.Items[SkillHorizonContext.CorrelationItem]?.ToString();

    private sealed record SignInRequest(string? SubjectId);

    private sealed record WorkspaceRequest(string? Workspace);

    private sealed record ShellIdentity(string SubjectId, string LegalEntityId, string DisplayName);

    private sealed record ShellState(
        bool Authenticated,
        bool LocalDevelopment,
        string? SubjectId,
        string? DisplayName,
        string? LegalEntityId,
        string Status,
        string? Reason,
        IReadOnlyList<string> Workspaces,
        string? ActiveWorkspace,
        IReadOnlyList<ShellIdentity> Identities,
        string? CorrelationId,
        string RequestToken);
}
