using System.Security.Cryptography;
using System.Text;

namespace SkillHorizon.Hosting;

public static class LearningServiceGate
{
    public const string Item = "learning_service";

    public const string Header = "X-Learning-Key";

    public static async Task<bool> AllowAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/internal"))
            return false;

        var provided = context.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(provided))
            return false;

        var options = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<SkillHorizonOptions>>().Value;
        var secret = await context.RequestServices.GetRequiredService<ISecretStore>()
            .GetRequiredAsync(options.LearningServiceKeySecretName, context.RequestAborted)
            .ConfigureAwait(false);
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(secret);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
