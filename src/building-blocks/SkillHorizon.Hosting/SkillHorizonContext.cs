namespace SkillHorizon.Hosting;

public static class SkillHorizonContext
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string CorrelationItem = "correlation_id";
    public const string ZoneItem = "zone";
    public const string WorkspaceHeader = "X-Active-Workspace";
    public const string RegistryKeyHeader = "X-Registry-Key";

    public static bool IsValidCorrelationId(string value)
    {
        if (value.Length is < 1 or > 128)
            return false;

        foreach (var character in value)
        {
            if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or ':' or '-')
                continue;

            return false;
        }

        return true;
    }
}
