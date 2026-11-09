namespace SkillHorizon.Hosting;

public static class DevelopmentIdentityParser
{
    public static IReadOnlyList<DevelopmentIdentity> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var identities = new List<DevelopmentIdentity>();
        foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('|');
            if (parts.Length != 3 || parts.Any(part => string.IsNullOrWhiteSpace(part)))
            {
                throw new InvalidOperationException(
                    $"Development identity '{entry}' must be subject|legalEntity|displayName.");
            }

            identities.Add(new DevelopmentIdentity(parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }

        return identities;
    }
}
