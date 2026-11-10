using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using SkillHorizon.Hosting;
using SkillHorizon.Web;

var builder = WebApplication.CreateBuilder(args);
var options = builder.BindSkillHorizonOptions();
if (string.IsNullOrWhiteSpace(options.ApiUrl))
    throw new InvalidOperationException("SkillHorizon:ApiUrl is required.");
if (string.IsNullOrWhiteSpace(options.LearningUrl))
    throw new InvalidOperationException("SkillHorizon:LearningUrl is required.");

builder.AddSkillHorizonTelemetry("skillhorizon-web");
builder.Services.AddSkillHorizonSecrets();
builder.Services.AddSkillHorizonIdentity(options);
builder.Services.AddAntiforgery(antiforgery =>
{
    antiforgery.HeaderName = "RequestVerificationToken";
    antiforgery.Cookie.Name = "skillhorizon.antiforgery";
    antiforgery.Cookie.HttpOnly = true;
    antiforgery.Cookie.SameSite = SameSiteMode.Lax;
    antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddAuthorization();

var authentication = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.Cookie.Name = "skillhorizon.session";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookie.LoginPath = "/signin";
        cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
        cookie.SlidingExpiration = true;
    });

if (!string.IsNullOrWhiteSpace(options.Oidc.Authority))
{
    var clientSecret = new ConfigurationSecretStore(builder.Configuration)
        .GetRequiredAsync(options.Oidc.ClientSecretName, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    authentication.AddOpenIdConnect(oidc =>
    {
        oidc.Authority = options.Oidc.Authority;
        oidc.ClientId = options.Oidc.ClientId;
        oidc.ClientSecret = clientSecret;
        oidc.ResponseType = "code";
        oidc.CallbackPath = options.Oidc.CallbackPath;
        oidc.SaveTokens = true;
        oidc.GetClaimsFromUserInfoEndpoint = true;
        oidc.MapInboundClaims = false;
        oidc.Scope.Add("profile");
    });
}

builder.Services.AddHttpClient<CoreApiClient>((serviceProvider, client) =>
{
    var apiUrl = serviceProvider.GetRequiredService<IOptions<SkillHorizonOptions>>().Value.ApiUrl;
    client.BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<LearningApiClient>((serviceProvider, client) =>
{
    var learningUrl = serviceProvider.GetRequiredService<IOptions<SkillHorizonOptions>>().Value.LearningUrl;
    client.BaseAddress = new Uri(learningUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHealthChecks();

var app = builder.Build();
_ = app.Services.GetRequiredService<IIdentityProvider>();
app.UseSkillHorizonRequestLogging();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapSkillHorizonShell();
app.Run();

public partial class Program;
