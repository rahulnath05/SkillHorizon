using System.Security.Claims;

namespace SkillHorizon.Hosting;

public static class SkillHorizonAuthenticationExtensions
{
    public static WebApplication UseSkillHorizonAuthentication(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (await LearningServiceGate.AllowAsync(context).ConfigureAwait(false))
            {
                context.Items[LearningServiceGate.Item] = true;
                await next().ConfigureAwait(false);
                return;
            }

            if (await RegistryGate.AllowAsync(context).ConfigureAwait(false))
            {
                context.Items[RegistryGate.Item] = true;
                await next().ConfigureAwait(false);
                return;
            }

            var header = context.Request.Headers.Authorization.ToString();
            var bearer = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : null;
            var provider = context.RequestServices.GetRequiredService<IIdentityProvider>();
            var assertion = await provider.ReadBearerAsync(bearer, context.RequestAborted).ConfigureAwait(false);
            if (assertion is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var identity = new ClaimsIdentity(
            [
                new Claim("sub", assertion.SubjectId),
                new Claim("employee_id", assertion.EmployeeId),
                new Claim("legal_entity", assertion.LegalEntityId)
            ], provider.Name);
            context.User = new ClaimsPrincipal(identity);
            await next().ConfigureAwait(false);
        });

        return app;
    }
}
