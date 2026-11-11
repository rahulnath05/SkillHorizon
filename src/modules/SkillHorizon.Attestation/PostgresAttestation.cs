using System.Text.Json;
using Npgsql;
using SkillHorizon.Audit;

namespace SkillHorizon.Attestation;

public sealed class PostgresPracticeScope : IPracticeScope
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPracticeScope(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<bool> GrantAsync(string subjectId, string practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var exists = new NpgsqlCommand("SELECT 1 FROM taxonomy.practice WHERE code = $1", connection);
        exists.Parameters.AddWithValue(practiceCode);
        if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            return false;

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO access.practice_scope (subject_id, practice_code, granted_at)
            VALUES ($1, $2, now())
            ON CONFLICT (subject_id, practice_code) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(practiceCode);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> HoldsAsync(string subjectId, string practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM access.practice_scope WHERE subject_id = $1 AND practice_code = $2",
            connection);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(practiceCode);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }
}

public sealed class PostgresAttestationPacks : IAttestationPacks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Classifications = new(StringComparer.Ordinal) { "aligned", "tightening", "new_bar" };

    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;

    public PostgresAttestationPacks(NpgsqlDataSource dataSource, IAuditLog audit)
    {
        _dataSource = dataSource;
        _audit = audit;
    }

    public async Task<PackView> SubmitAsync(
        string subjectId,
        string correlationId,
        string practiceCode,
        IReadOnlyList<PackRowWrite> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            throw new InvalidOperationException("A pack needs at least one profile row.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await PracticeExists(connection, transaction, practiceCode, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("That practice is not in the taxonomy.");

        var packId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO attestation.pack (pack_id, practice_code, status, submitted_by, submitted_at)
            VALUES ($1, $2, 'submitted', $3, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(packId);
            insert.Parameters.AddWithValue(practiceCode);
            insert.Parameters.AddWithValue(subjectId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var seen = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!seen.Add(row.JobProfileId))
                throw new InvalidOperationException("A profile can appear once in a pack.");
            if (!Classifications.Contains(row.Classification))
                throw new InvalidOperationException("A pack row is aligned, tightening, or a new bar.");
            var note = string.IsNullOrWhiteSpace(row.ExceptionNote) ? null : row.ExceptionNote.Trim();
            if (row.Classification == "aligned" && note is not null)
                throw new InvalidOperationException("An aligned profile has no exception.");
            if (row.Classification != "aligned" && note is null)
                throw new InvalidOperationException("A tightening or new-bar profile records its exception.");

            var profile = await Profile(connection, transaction, row.JobProfileId, cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO attestation.pack_row (
                    row_id, pack_id, job_profile_id, profile_code, profile_name, classification, exception_note)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(packId);
            command.Parameters.AddWithValue(row.JobProfileId);
            command.Parameters.AddWithValue(profile.Code);
            command.Parameters.AddWithValue(profile.Name);
            command.Parameters.AddWithValue(row.Classification);
            command.Parameters.AddWithValue(note is null ? DBNull.Value : note);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(subjectId, "pack.submitted", "attestation.pack", packId, correlationId, JsonSerializer.Serialize(new { practiceCode, rows = rows.Count }, Json)),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await Read(packId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackView> SealAsync(string subjectId, string correlationId, Guid packId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string practiceCode;
        string status;
        await using (var pack = new NpgsqlCommand(
            "SELECT practice_code, status FROM attestation.pack WHERE pack_id = $1 FOR UPDATE",
            connection,
            transaction))
        {
            pack.Parameters.AddWithValue(packId);
            await using var reader = await pack.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("That pack does not exist.");
            practiceCode = reader.GetString(0);
            status = reader.GetString(1);
        }

        if (!string.Equals(status, "submitted", StringComparison.Ordinal))
            throw new InvalidOperationException("That pack is already sealed.");

        await using (var scope = new NpgsqlCommand(
            "SELECT 1 FROM access.practice_scope WHERE subject_id = $1 AND practice_code = $2",
            connection,
            transaction))
        {
            scope.Parameters.AddWithValue(subjectId);
            scope.Parameters.AddWithValue(practiceCode);
            if (await scope.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidOperationException("That practice is outside this lead's scope.");
        }

        Guid taxonomyVersionId;
        await using (var taxonomy = new NpgsqlCommand(
            "SELECT taxonomy_version_id FROM taxonomy.version WHERE status = 'published'",
            connection,
            transaction))
        {
            var found = await taxonomy.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is not Guid published)
                throw new InvalidOperationException("A seal needs the published taxonomy version.");
            taxonomyVersionId = published;
        }

        var profiles = new List<Guid>();
        await using (var rows = new NpgsqlCommand(
            "SELECT job_profile_id FROM attestation.pack_row WHERE pack_id = $1 ORDER BY profile_name",
            connection,
            transaction))
        {
            rows.Parameters.AddWithValue(packId);
            await using var reader = await rows.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                profiles.Add(reader.GetGuid(0));
        }

        var sealId = Guid.NewGuid();
        DateTimeOffset sealedAt;
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO attestation.seal (seal_id, pack_id, taxonomy_version_id, lead_subject_id, sealed_at)
            VALUES ($1, $2, $3, $4, now())
            RETURNING sealed_at
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(sealId);
            insert.Parameters.AddWithValue(packId);
            insert.Parameters.AddWithValue(taxonomyVersionId);
            insert.Parameters.AddWithValue(subjectId);
            sealedAt = Timestamp(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }

        foreach (var profileId in profiles)
        {
            await using var published = new NpgsqlCommand(
                """
                SELECT benchmark_version_id FROM benchmark.version
                WHERE job_profile_id = $1 AND status = 'published'
                """,
                connection,
                transaction);
            published.Parameters.AddWithValue(profileId);
            var benchmarkId = await published.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (benchmarkId is not Guid versionId)
                throw new InvalidOperationException("A profile in this pack has no published benchmark.");

            await using var link = new NpgsqlCommand(
                """
                INSERT INTO attestation.sealed_benchmark (seal_id, job_profile_id, benchmark_version_id)
                VALUES ($1, $2, $3)
                """,
                connection,
                transaction);
            link.Parameters.AddWithValue(sealId);
            link.Parameters.AddWithValue(profileId);
            link.Parameters.AddWithValue(versionId);
            await link.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE attestation.pack SET status = 'sealed' WHERE pack_id = $1",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue(packId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var baseline = new NpgsqlCommand(
            """
            INSERT INTO attestation.baseline (practice_code, pack_id, taxonomy_version_id, sealed_at)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (practice_code) DO UPDATE
                SET pack_id = EXCLUDED.pack_id,
                    taxonomy_version_id = EXCLUDED.taxonomy_version_id,
                    sealed_at = EXCLUDED.sealed_at
            """,
            connection,
            transaction))
        {
            baseline.Parameters.AddWithValue(practiceCode);
            baseline.Parameters.AddWithValue(packId);
            baseline.Parameters.AddWithValue(taxonomyVersionId);
            baseline.Parameters.AddWithValue(sealedAt);
            await baseline.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(
                subjectId,
                "pack.sealed",
                "attestation.seal",
                sealId,
                correlationId,
                JsonSerializer.Serialize(new { packId, practiceCode, taxonomyVersionId }, Json)),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await Read(packId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PackView>> ListAsync(string? practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT pack_id FROM attestation.pack
            WHERE $1::text IS NULL OR practice_code = $1
            ORDER BY submitted_at DESC
            """,
            connection))
        {
            command.Parameters.AddWithValue(practiceCode is null ? DBNull.Value : practiceCode);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids.Add(reader.GetGuid(0));
        }

        var packs = new List<PackView>();
        foreach (var id in ids)
            packs.Add(await Read(connection, id, cancellationToken).ConfigureAwait(false));
        return packs;
    }

    private async Task<PackView> Read(Guid packId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await Read(connection, packId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PackView> Read(NpgsqlConnection connection, Guid packId, CancellationToken cancellationToken)
    {
        string practiceCode;
        string status;
        string submittedBy;
        Guid? taxonomyVersionId = null;
        string? lead = null;
        DateTimeOffset? sealedAt = null;
        await using (var pack = new NpgsqlCommand(
            """
            SELECT pack.practice_code, pack.status, pack.submitted_by,
                   seal.taxonomy_version_id, seal.lead_subject_id, seal.sealed_at
            FROM attestation.pack AS pack
            LEFT JOIN attestation.seal AS seal ON seal.pack_id = pack.pack_id
            WHERE pack.pack_id = $1
            """,
            connection))
        {
            pack.Parameters.AddWithValue(packId);
            await using var reader = await pack.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("That pack does not exist.");
            practiceCode = reader.GetString(0);
            status = reader.GetString(1);
            submittedBy = reader.GetString(2);
            if (!reader.IsDBNull(3))
            {
                taxonomyVersionId = reader.GetGuid(3);
                lead = reader.GetString(4);
                sealedAt = Timestamp(reader.GetValue(5));
            }
        }

        var rows = new List<PackRowView>();
        await using var command = new NpgsqlCommand(
            """
            SELECT row.row_id, row.job_profile_id, row.profile_code, row.profile_name,
                   row.classification, row.exception_note, sealed.benchmark_version_id
            FROM attestation.pack_row AS row
            LEFT JOIN attestation.seal AS seal ON seal.pack_id = row.pack_id
            LEFT JOIN attestation.sealed_benchmark AS sealed
                ON sealed.seal_id = seal.seal_id AND sealed.job_profile_id = row.job_profile_id
            WHERE row.pack_id = $1
            ORDER BY row.profile_name
            """,
            connection);
        command.Parameters.AddWithValue(packId);
        await using var rowReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rowReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PackRowView(
                rowReader.GetGuid(0),
                rowReader.GetGuid(1),
                rowReader.GetString(2),
                rowReader.GetString(3),
                rowReader.GetString(4),
                rowReader.IsDBNull(5) ? null : rowReader.GetString(5),
                rowReader.IsDBNull(6) ? null : rowReader.GetGuid(6)));
        }

        return new PackView(packId, practiceCode, status, submittedBy, taxonomyVersionId, lead, sealedAt, rows);
    }

    private static async Task<bool> PracticeExists(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string practiceCode,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM taxonomy.practice WHERE code = $1", connection, transaction);
        command.Parameters.AddWithValue(practiceCode);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<(string Code, string Name)> Profile(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT code, display_name FROM job_architecture.job_profile WHERE profile_id = $1",
            connection,
            transaction);
        command.Parameters.AddWithValue(profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("That job profile does not exist.");
        return (reader.GetString(0), reader.GetString(1));
    }

    private static DateTimeOffset Timestamp(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A timestamp was not returned.")
    };
}

public sealed class PostgresCohorts : ICohorts
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;

    public PostgresCohorts(NpgsqlDataSource dataSource, IAuditLog audit)
    {
        _dataSource = dataSource;
        _audit = audit;
    }

    public async Task SetMaximumAsync(string zoneId, int maximum, CancellationToken cancellationToken)
    {
        if (maximum < 1)
            throw new InvalidOperationException("The operating maximum is at least one person.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO attestation.operating_limit (zone_id, maximum)
            VALUES ($1, $2)
            ON CONFLICT (zone_id) DO UPDATE SET maximum = EXCLUDED.maximum
            """,
            connection);
        command.Parameters.AddWithValue(zoneId);
        command.Parameters.AddWithValue(maximum);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CohortView> CreateAsync(
        string zoneId,
        string subjectId,
        string correlationId,
        string practiceCode,
        string? countryCode,
        string? orgUnit,
        Guid jobProfileId,
        IReadOnlyList<string> memberIds,
        CancellationToken cancellationToken)
    {
        var country = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim();
        var unit = string.IsNullOrWhiteSpace(orgUnit) ? null : orgUnit.Trim();
        if (country is null && unit is null)
            throw new InvalidOperationException("A cohort sits in a country or an org unit.");
        var members = memberIds
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (members.Length == 0 || members.Any(id => id == "*"))
            throw new InvalidOperationException("A cohort names the people in it. It is not everyone on the job profile.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var maximum = await LockMaximum(connection, transaction, zoneId, cancellationToken).ConfigureAwait(false);
        if (members.Length > maximum)
            throw new InvalidOperationException("That cohort would pass the operating maximum.");

        await using (var scope = new NpgsqlCommand(
            "SELECT 1 FROM access.practice_scope WHERE subject_id = $1 AND practice_code = $2",
            connection,
            transaction))
        {
            scope.Parameters.AddWithValue(subjectId);
            scope.Parameters.AddWithValue(practiceCode);
            if (await scope.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidOperationException("That practice is outside this lead's scope.");
        }

        await using (var profile = new NpgsqlCommand(
            "SELECT 1 FROM job_architecture.job_profile WHERE profile_id = $1",
            connection,
            transaction))
        {
            profile.Parameters.AddWithValue(jobProfileId);
            if (await profile.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidOperationException("That job profile does not exist.");
        }

        var cohortId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO attestation.cohort (
                cohort_id, practice_code, country_code, org_unit, job_profile_id, lead_subject_id, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(cohortId);
            insert.Parameters.AddWithValue(practiceCode);
            insert.Parameters.AddWithValue(country is null ? DBNull.Value : country);
            insert.Parameters.AddWithValue(unit is null ? DBNull.Value : unit);
            insert.Parameters.AddWithValue(jobProfileId);
            insert.Parameters.AddWithValue(subjectId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var member in members)
        {
            await using var link = new NpgsqlCommand(
                "INSERT INTO attestation.cohort_member (cohort_id, employee_id) VALUES ($1, $2)",
                connection,
                transaction);
            link.Parameters.AddWithValue(cohortId);
            link.Parameters.AddWithValue(member);
            await link.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(subjectId, "cohort.created", "attestation.cohort", cohortId, correlationId, "{\"members\":" + members.Length + "}"),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CohortView(cohortId, practiceCode, country, unit, jobProfileId, members.Length);
    }

    private static async Task<int> LockMaximum(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string zoneId,
        CancellationToken cancellationToken)
    {
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO attestation.operating_limit (zone_id, maximum)
            VALUES ($1, 25)
            ON CONFLICT (zone_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(zoneId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = new NpgsqlCommand(
            "SELECT maximum FROM attestation.operating_limit WHERE zone_id = $1 FOR UPDATE",
            connection,
            transaction);
        command.Parameters.AddWithValue(zoneId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }
}
