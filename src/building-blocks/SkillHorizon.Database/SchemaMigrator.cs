using DbUp;
using DbUp.Engine;

namespace SkillHorizon.Database;

public static class SchemaMigrator
{
    public static void ApplyDomain(string connectionString) =>
        Apply(connectionString, ".Scripts.Domain.");

    public static void ApplyControl(string connectionString) =>
        Apply(connectionString, ".Scripts.Control.");

    private static void Apply(string connectionString, string nameSegment)
    {
        var assembly = typeof(SchemaMigrator).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(nameSegment, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new SqlScript(name, Read(assembly, name)))
            .ToList();

        if (scripts.Count == 0)
        {
            throw new InvalidOperationException(
                "No migration scripts were embedded for '" + nameSegment + "'. Found: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        }

        var upgrade = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScripts(scripts)
            .JournalToPostgresqlTable("public", "schema_versions")
            .WithTransactionPerScript()
            .Build();

        var result = upgrade.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Migration failed on {result.ErrorScript?.Name}: {result.Error?.Message}",
                result.Error);
        }
    }

    private static string Read(System.Reflection.Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded script {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
