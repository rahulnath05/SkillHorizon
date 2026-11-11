using System.Security.Cryptography;
using System.Text;

namespace SkillHorizon.Hosting;

public static class RegistryGate
{
    public const string Item = "registry_service";

    public static async Task<bool> AllowAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/aggregates"))
            return false;

        var provided = context.Request.Headers[SkillHorizonContext.RegistryKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(provided))
            return false;

        var options = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<SkillHorizonOptions>>().Value;
        var secret = await context.RequestServices.GetRequiredService<ISecretStore>()
            .GetRequiredAsync(options.RegistryKeySecretName, context.RequestAborted)
            .ConfigureAwait(false);
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(secret);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
