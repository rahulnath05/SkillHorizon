using DbUp;
using DbUp.Engine;
using Npgsql;

namespace SkillHorizon.Database;

public static class SchemaMigrator
{
    public static void ApplyDomain(string connectionString)
    {
        EnsureDatabase(connectionString);
        Apply(connectionString, ".Scripts.Domain.");
    }

    public static void ApplyCatalogueSeed(string connectionString) =>
        Apply(connectionString, ".Scripts.Seed.");

    public static void ApplyControl(string connectionString) =>
        Apply(connectionString, ".Scripts.Control.");

    public static void ApplyAssessment(string connectionString) =>
        Apply(connectionString, ".Scripts.Assessment.");

    public static void ApplyProgress(string connectionString) =>
        Apply(connectionString, ".Scripts.Progress.");

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

        var builder = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScripts(scripts)
            .JournalToPostgresqlTable("public", "schema_versions")
            .WithTransactionPerScript();
        builder.Configure(configuration => configuration.VariablesEnabled = false);
        var upgrade = builder.Build();

        var result = upgrade.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Migration failed on {result.ErrorScript?.Name}: {result.Error?.Message}",
                result.Error);
        }
    }

    private static void EnsureDatabase(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database)
            || database.Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return;

        builder.Database = "postgres";
        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();
        using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", database);
        if (exists.ExecuteScalar() is not null)
            return;

        var identifier = "\"" + database.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        using var create = new NpgsqlCommand("CREATE DATABASE " + identifier, connection);
        create.ExecuteNonQuery();
    }

    private static string Read(System.Reflection.Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded script {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
