using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
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
        app.MapGet("/signin", (Delegate)SignInPage);
        app.MapPost("/signin", (Delegate)SignIn);
        app.MapPost("/signout", (Delegate)SignOut);
        app.MapGet("/", (Delegate)Enter);
        app.MapGet("/entry", (Delegate)EntryPage);
        app.MapPost("/entry", (Delegate)Choose);
        app.MapGet("/workspace", (Delegate)WorkspacePage);
        app.MapGet("/refused", (Delegate)RefusedPage);
        return app;
    }

    private static async Task<IResult> SignInPage(HttpContext http)
    {
        var identity = http.RequestServices.GetRequiredService<IIdentityProvider>();
        if (!identity.IsLocalDevelopment)
        {
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = "/" },
                [OpenIdConnectDefaults.AuthenticationScheme]);
        }

        if (http.User.Identity?.IsAuthenticated == true)
            return Results.Redirect("/");

        return ShellHtml.Page(
            "Sign in",
            SignInMain(http, identity.DevelopmentIdentities, null),
            Correlation(http),
            null);
    }

    private static async Task<IResult> SignIn(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest();

        var identity = http.RequestServices.GetRequiredService<IIdentityProvider>();
        if (!identity.IsLocalDevelopment)
            return Results.Redirect("/signin");

        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var subject = form["subjectId"].ToString();
        var match = identity.FindDevelopmentIdentity(subject);
        if (match is null)
        {
            return ShellHtml.Page(
                "Sign in",
                SignInMain(http, identity.DevelopmentIdentities, "That identity is not in the local development directory."),
                Correlation(http),
                null);
        }

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
        return Results.Redirect("/");
    }

    private static async Task<IResult> SignOut(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest();

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Results.Redirect("/signin");
    }

    private static async Task<IResult> Enter(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Redirect("/signin");

        var lookup = await Lookup(http).ConfigureAwait(false);
        if (lookup.Status == WorkspaceLookupStatus.Unauthenticated)
            return Results.Redirect("/signin");
        if (lookup.Status == WorkspaceLookupStatus.Unavailable)
            return UnavailablePage(http);
        if (lookup.Status == WorkspaceLookupStatus.Refused)
            return Results.Redirect("/refused?reason=unmapped");
        if (lookup.Workspaces.Count == 0)
            return Results.Redirect("/refused?reason=grant");

        var active = http.User.FindFirst("active_workspace")?.Value;
        if (lookup.Workspaces.Count == 1)
        {
            if (active != lookup.Workspaces[0])
                await RememberWorkspace(http, lookup.Workspaces[0]).ConfigureAwait(false);
            return Results.Redirect("/workspace");
        }

        if (!string.IsNullOrWhiteSpace(active) && lookup.Workspaces.Contains(active, StringComparer.Ordinal))
            return Results.Redirect("/workspace");

        return Results.Redirect("/entry");
    }

    private static async Task<IResult> EntryPage(HttpContext http, string? error)
    {
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Redirect("/signin");

        var lookup = await Lookup(http).ConfigureAwait(false);
        if (lookup.Status != WorkspaceLookupStatus.Ok)
            return Results.Redirect("/");
        if (lookup.Workspaces.Count < 2)
            return Results.Redirect("/");

        return ShellHtml.Page(
            "Choose a workspace",
            EntryMain(http, lookup.Workspaces, error),
            Correlation(http),
            TokenFields(http));
    }

    private static async Task<IResult> Choose(HttpContext http)
    {
        if (!await IsValidForm(http).ConfigureAwait(false))
            return Results.BadRequest();
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Redirect("/signin");

        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var workspace = form["workspace"].ToString();
        var lookup = await Lookup(http).ConfigureAwait(false);
        if (lookup.Status != WorkspaceLookupStatus.Ok || lookup.Workspaces.Count < 2)
            return Results.Redirect("/");
        if (!lookup.Workspaces.Contains(workspace, StringComparer.Ordinal))
            return await EntryPage(http, "That workspace is not held.").ConfigureAwait(false);

        await RememberWorkspace(http, workspace).ConfigureAwait(false);
        return Results.Redirect("/workspace");
    }

    private static async Task<IResult> WorkspacePage(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Redirect("/signin");

        var lookup = await Lookup(http).ConfigureAwait(false);
        if (lookup.Status != WorkspaceLookupStatus.Ok)
            return Results.Redirect("/");

        var active = http.User.FindFirst("active_workspace")?.Value;
        if (string.IsNullOrWhiteSpace(active) || !lookup.Workspaces.Contains(active, StringComparer.Ordinal))
            return Results.Redirect(lookup.Workspaces.Count > 1 ? "/entry" : "/");

        var label = WorkspaceText.Label(active);
        var detail = WorkspaceText.Detail(active);
        var switcher = lookup.Workspaces.Count > 1
            ? """<p class="actions"><a class="text-button" href="/entry">Choose a different workspace</a></p>"""
            : "";
        var main = $"""
            <p class="hint">Active workspace</p>
            <h1>{ShellHtml.Encode(label)}</h1>
            <p class="lede">{ShellHtml.Encode(detail)}</p>
            <p class="lede">This shell is empty. The workspace opens here when its features are built.</p>
            {switcher}
            """;
        return ShellHtml.Page(label, main, Correlation(http), TokenFields(http));
    }

    private static IResult RefusedPage(HttpContext http, string? reason)
    {
        var unmapped = !string.Equals(reason, "grant", StringComparison.Ordinal);
        var title = unmapped ? "Not mapped to a zone" : "No workspace is held";
        var text = unmapped
            ? "You are not mapped to a zone. SkillHorizon will not place an unmapped person into another zone."
            : "No workspace is held for this sign-in, so there is nothing to open.";
        var signedIn = http.User.Identity?.IsAuthenticated == true;
        return ShellHtml.Page(
            title,
            $"""
             {ShellHtml.Banner("rose", text)}
             <h1>{ShellHtml.Encode(title)}</h1>
             <p class="lede">Sign out, then try a different identity if you are using the local development provider.</p>
             """,
            Correlation(http),
            signedIn ? TokenFields(http) : null);
    }

    private static IResult UnavailablePage(HttpContext http) =>
        ShellHtml.Page(
            "Routing unavailable",
            $"""
             {ShellHtml.Banner("amber", "Zone routing is unavailable. This sign-in was not placed in another zone.")}
             <h1>Routing unavailable</h1>
             <p class="lede">The control registry could not be reached. Try again in a moment.</p>
             <p class="actions"><a class="primary" href="/">Try again</a></p>
             """,
            Correlation(http),
            http.User.Identity?.IsAuthenticated == true ? TokenFields(http) : null);

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

    private static async Task RememberWorkspace(HttpContext http, string workspace)
    {
        var identity = (ClaimsIdentity)http.User.Identity!;
        var clone = identity.Clone();
        foreach (var claim in clone.FindAll("active_workspace").ToList())
            clone.RemoveClaim(claim);
        clone.AddClaim(new Claim("active_workspace", workspace));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(clone))
            .ConfigureAwait(false);
    }

    private static string SignInMain(HttpContext http, IReadOnlyList<DevelopmentIdentity> identities, string? error)
    {
        if (identities.Count == 0)
        {
            return """
                <div class="empty">
                  <div class="empty-mark" aria-hidden="true">ID</div>
                  <h1>Sign in</h1>
                  <p class="lede">No development identities are configured for the local identity provider.</p>
                </div>
                """;
        }

        var choices = new StringBuilder();
        foreach (var identity in identities)
        {
            var subject = ShellHtml.Encode(identity.SubjectId);
            var legalEntity = ShellHtml.Encode(identity.LegalEntityId);
            choices.Append($$"""
                <label class="choice">
                  <input type="radio" name="subjectId" value="{{subject}}" required>
                  <span>
                    <span class="choice-title">{{subject}}</span>
                    <span class="choice-copy">Legal entity {{legalEntity}}</span>
                  </span>
                </label>
                """);
        }

        var alert = string.IsNullOrWhiteSpace(error) ? "" : ShellHtml.Banner("rose", error);
        return $"""
            {alert}
            <h1>Sign in</h1>
            <p class="lede">Local development identity provider. This chooses who you are. It does not choose a workspace.</p>
            <form method="post" action="/signin">
              {TokenFields(http)}
              <div class="stack">{choices}</div>
              <div class="actions"><button type="submit" class="primary">Continue</button></div>
            </form>
            """;
    }

    private static string EntryMain(HttpContext http, IReadOnlyList<string> workspaces, string? error)
    {
        var choices = new StringBuilder();
        foreach (var workspace in workspaces)
        {
            var code = ShellHtml.Encode(workspace);
            choices.Append($$"""
                <button type="submit" class="choice" name="workspace" value="{{code}}">
                  <span>
                    <span class="choice-title">{{ShellHtml.Encode(WorkspaceText.Label(workspace))}}</span>
                    <span class="choice-copy">{{ShellHtml.Encode(WorkspaceText.Detail(workspace))}}</span>
                  </span>
                </button>
                """);
        }

        var alert = string.IsNullOrWhiteSpace(error)
            ? ShellHtml.Banner("info", "You hold more than one workspace. Enter the one you are working in now.")
            : ShellHtml.Banner("rose", error);
        return $"""
            {alert}
            <h1>Choose a workspace</h1>
            <p class="lede">The list is only the workspaces this sign-in holds.</p>
            <form method="post" action="/entry">
              {TokenFields(http)}
              <div class="stack">{choices}</div>
            </form>
            """;
    }

    private static string TokenFields(HttpContext http)
    {
        var tokens = http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
        var name = HtmlEncoder.Default.Encode(tokens.FormFieldName ?? "");
        var value = HtmlEncoder.Default.Encode(tokens.RequestToken ?? "");
        return $"""<input type="hidden" name="{name}" value="{value}">""";
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
}
