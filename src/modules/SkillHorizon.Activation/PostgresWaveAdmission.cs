using System.Security.Cryptography;
using System.Text;
using Npgsql;
using SkillHorizon.Audit;

namespace SkillHorizon.Activation;

public sealed class PostgresWaveAdmission : IWaveAdmission
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuditLog _audit;

    public PostgresWaveAdmission(NpgsqlDataSource dataSource, IAuditLog audit)
    {
        _dataSource = dataSource;
        _audit = audit;
    }

    public async Task EnsureBudgetAsync(string zoneId, int sittingBudget, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO activation.capacity (zone_id, sitting_budget)
            VALUES ($1, $2)
            ON CONFLICT (zone_id) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue(zoneId);
        command.Parameters.AddWithValue(sittingBudget);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetBudgetAsync(string zoneId, int sittingBudget, CancellationToken cancellationToken)
    {
        if (sittingBudget < 1)
            throw new InvalidOperationException("The zone budget must admit at least one person.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO activation.capacity (zone_id, sitting_budget)
            VALUES ($1, $2)
            ON CONFLICT (zone_id) DO UPDATE SET sitting_budget = EXCLUDED.sitting_budget
            """,
            connection);
        command.Parameters.AddWithValue(zoneId);
        command.Parameters.AddWithValue(sittingBudget);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WaveAdmission> AdmitAsync(
        string zoneId,
        Guid benchmarkVersionId,
        IReadOnlyList<string> subjectIds,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var people = subjectIds
            .Select(subject => subject.Trim())
            .Where(subject => subject.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (people.Length == 0)
            throw new InvalidOperationException("A wave needs a population.");

        var profileName = await ProfileName(benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var budget = await LockBudget(connection, transaction, zoneId, cancellationToken).ConfigureAwait(false);
        if (people.Length > budget)
            throw new InvalidOperationException("That population is over the zone budget.");

        var waveId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO activation.wave (wave_id, benchmark_version_id, population, created_at)
            VALUES ($1, $2, $3, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(waveId);
            insert.Parameters.AddWithValue(benchmarkVersionId);
            insert.Parameters.AddWithValue(people.Length);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var subject in people)
        {
            await using var admit = new NpgsqlCommand(
                """
                INSERT INTO activation.admission (wave_id, subject_id, admitted_at, route)
                VALUES ($1, $2, now(), 'launch')
                """,
                connection,
                transaction);
            admit.Parameters.AddWithValue(waveId);
            admit.Parameters.AddWithValue(subject);
            await admit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(actorId, "wave.admitted", "activation.wave", waveId, correlationId, $"{{\"population\":{people.Length}}}"),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new WaveAdmission(waveId, benchmarkVersionId, profileName, people.Length);
    }

    public async Task<ActivationCentre> ReadCentreAsync(string zoneId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        int? budget = null;
        await using (var command = new NpgsqlCommand(
            "SELECT sitting_budget FROM activation.capacity WHERE zone_id = $1",
            connection))
        {
            command.Parameters.AddWithValue(zoneId);
            var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is int sittingBudget)
                budget = sittingBudget;
        }

        var waves = new List<WaveListing>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT wave.wave_id, wave.benchmark_version_id, profile.display_name, wave.population,
                   (SELECT count(*) FROM activation.admission AS admission WHERE admission.wave_id = wave.wave_id),
                   wave.created_at
            FROM activation.wave AS wave
            JOIN benchmark.version AS version ON version.benchmark_version_id = wave.benchmark_version_id
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            ORDER BY wave.created_at DESC
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                waves.Add(new WaveListing(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    Convert.ToInt32(reader.GetValue(4)),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    []));
            }
        }

        for (var index = 0; index < waves.Count; index++)
        {
            var subjects = new List<string>();
            await using var command = new NpgsqlCommand(
                """
                SELECT subject_id
                FROM activation.admission
                WHERE wave_id = $1
                ORDER BY admitted_at, subject_id
                """,
                connection);
            command.Parameters.AddWithValue(waves[index].WaveId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                subjects.Add(reader.GetString(0));
            waves[index] = waves[index] with { SubjectIds = subjects };
        }

        return new ActivationCentre(budget, waves);
    }

    public async Task<ZonePerson?> FindPersonAsync(string employeeId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT employee_id, legal_entity_id, employment_status, joined_at
            FROM workforce.person
            WHERE employee_id = $1
            """,
            connection);
        command.Parameters.AddWithValue(employeeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new ZonePerson(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task<WaveAdmission> AdmitRollingAsync(
        string zoneId,
        Guid waveId,
        string subjectId,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var personId = subjectId.Trim();
        if (personId.Length == 0)
            throw new InvalidOperationException("Rolling intake admits one person.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var budget = await LockBudget(connection, transaction, zoneId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset openedAt;
        Guid benchmarkVersionId;
        await using (var wave = new NpgsqlCommand(
            """
            SELECT benchmark_version_id, created_at
            FROM activation.wave
            WHERE wave_id = $1
            FOR UPDATE
            """,
            connection,
            transaction))
        {
            wave.Parameters.AddWithValue(waveId);
            await using var reader = await wave.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("That wave is not open.");
            benchmarkVersionId = reader.GetGuid(0);
            openedAt = reader.GetFieldValue<DateTimeOffset>(1);
        }

        string employment;
        DateTimeOffset? joinedAt;
        await using (var person = new NpgsqlCommand(
            """
            SELECT employment_status, joined_at
            FROM workforce.person
            WHERE employee_id = $1
            """,
            connection,
            transaction))
        {
            person.Parameters.AddWithValue(personId);
            await using var reader = await person.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("That person is not in this zone.");
            employment = reader.GetString(0);
            joinedAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        }

        if (employment != "active")
            throw new InvalidOperationException("A closed person is not admitted.");
        if (joinedAt is null || joinedAt <= openedAt)
            throw new InvalidOperationException("That person joined before this wave opened.");

        await using (var existing = new NpgsqlCommand(
            "SELECT 1 FROM activation.admission WHERE wave_id = $1 AND subject_id = $2",
            connection,
            transaction))
        {
            existing.Parameters.AddWithValue(waveId);
            existing.Parameters.AddWithValue(personId);
            if (await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("That person is already admitted to this wave.");
        }

        int admitted;
        await using (var count = new NpgsqlCommand(
            "SELECT count(*) FROM activation.admission WHERE wave_id = $1",
            connection,
            transaction))
        {
            count.Parameters.AddWithValue(waveId);
            admitted = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }

        if (admitted + 1 > budget)
            throw new InvalidOperationException("That population is over the zone budget.");

        await using (var admit = new NpgsqlCommand(
            """
            INSERT INTO activation.admission (wave_id, subject_id, admitted_at, route)
            VALUES ($1, $2, now(), 'rolling')
            """,
            connection,
            transaction))
        {
            admit.Parameters.AddWithValue(waveId);
            admit.Parameters.AddWithValue(personId);
            await admit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var grow = new NpgsqlCommand(
            "UPDATE activation.wave SET population = population + 1 WHERE wave_id = $1",
            connection,
            transaction))
        {
            grow.Parameters.AddWithValue(waveId);
            await grow.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            connection,
            transaction,
            new AuditEntry(actorId, "wave.rolling", "activation.wave", waveId, correlationId, "{\"admitted\":1}"),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var profileName = await ProfileName(benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        return new WaveAdmission(waveId, benchmarkVersionId, profileName, admitted + 1);
    }

    public async Task<IssuedSitting> AuthorizeAsync(Guid waveId, string subjectId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Guid benchmarkVersionId;
        await using (var command = new NpgsqlCommand(
            """
            SELECT wave.benchmark_version_id
            FROM activation.admission AS admission
            JOIN activation.wave AS wave ON wave.wave_id = admission.wave_id
            WHERE admission.wave_id = $1 AND admission.subject_id = $2
            """,
            connection))
        {
            command.Parameters.AddWithValue(waveId);
            command.Parameters.AddWithValue(subjectId);
            var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is not Guid versionId)
                throw new InvalidOperationException("This person is not admitted to that wave.");
            benchmarkVersionId = versionId;
        }

        await using (var sealedAlready = new NpgsqlCommand(
            """
            SELECT 1
            FROM readiness.sealed_result
            WHERE subject_id = $1 AND wave_id = $2
            """,
            connection))
        {
            sealedAlready.Parameters.AddWithValue(subjectId);
            sealedAlready.Parameters.AddWithValue(waveId);
            if (await sealedAlready.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("This wave already has a sealed result.");
        }

        var skills = await Skills(connection, benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        var scale = await Scale(connection, benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var authorizationId = Guid.NewGuid();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO activation.sitting_authorization (
                authorization_id, token_hash, subject_id, wave_id, benchmark_version_id, created_at, expires_at)
            VALUES ($1, $2, $3, $4, $5, now(), now() + interval '2 hours')
            """,
            connection);
        insert.Parameters.AddWithValue(authorizationId);
        insert.Parameters.AddWithValue(Hash(token));
        insert.Parameters.AddWithValue(subjectId);
        insert.Parameters.AddWithValue(waveId);
        insert.Parameters.AddWithValue(benchmarkVersionId);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var grant = new SittingGrant(authorizationId, subjectId, waveId, benchmarkVersionId, skills, scale);
        return new IssuedSitting(grant, token);
    }

    public async Task<SittingGrant?> ReadGrantAsync(string token, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Guid authorizationId;
        string subjectId;
        Guid waveId;
        Guid benchmarkVersionId;
        await using (var command = new NpgsqlCommand(
            """
            SELECT authorization_id, subject_id, wave_id, benchmark_version_id
            FROM activation.sitting_authorization
            WHERE token_hash = $1 AND consumed_at IS NULL AND expires_at > now()
            """,
            connection))
        {
            command.Parameters.AddWithValue(Hash(token));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            authorizationId = reader.GetGuid(0);
            subjectId = reader.GetString(1);
            waveId = reader.GetGuid(2);
            benchmarkVersionId = reader.GetGuid(3);
        }

        var skills = await Skills(connection, benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        var scale = await Scale(connection, benchmarkVersionId, cancellationToken).ConfigureAwait(false);
        return new SittingGrant(authorizationId, subjectId, waveId, benchmarkVersionId, skills, scale);
    }

    public async Task<WaveAdmission?> CurrentWaveAsync(string subjectId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT wave.wave_id, wave.benchmark_version_id, profile.display_name, wave.population
            FROM activation.admission AS admission
            JOIN activation.wave AS wave ON wave.wave_id = admission.wave_id
            JOIN benchmark.version AS version ON version.benchmark_version_id = wave.benchmark_version_id
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            WHERE admission.subject_id = $1
            ORDER BY wave.created_at DESC
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue(subjectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new WaveAdmission(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3));
    }

    private async Task<string> ProfileName(Guid benchmarkVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT profile.display_name
            FROM benchmark.version AS version
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            WHERE version.benchmark_version_id = $1 AND version.status IN ('published', 'superseded')
            """,
            connection);
        command.Parameters.AddWithValue(benchmarkVersionId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is not string name)
            throw new InvalidOperationException("A wave is admitted against a published benchmark version.");
        return name;
    }

    private static async Task<int> LockBudget(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string zoneId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT sitting_budget FROM activation.capacity WHERE zone_id = $1 FOR UPDATE",
            connection,
            transaction);
        command.Parameters.AddWithValue(zoneId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is not int budget)
            throw new InvalidOperationException("The zone budget is not configured.");
        return budget;
    }

    private static async Task<IReadOnlyList<AuthorizedSkill>> Skills(
        NpgsqlConnection connection,
        Guid benchmarkVersionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT skill_id, skill_code, skill_name, proficiency_rank, proficiency_code, proficiency_name
            FROM benchmark.target
            WHERE benchmark_version_id = $1
            ORDER BY skill_name
            """,
            connection);
        command.Parameters.AddWithValue(benchmarkVersionId);
        var skills = new List<AuthorizedSkill>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            skills.Add(new AuthorizedSkill(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        if (skills.Count == 0)
            throw new InvalidOperationException("That benchmark version has no skill targets.");
        return skills;
    }

    private static async Task<IReadOnlyList<AuthorizedLevel>> Scale(
        NpgsqlConnection connection,
        Guid benchmarkVersionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT proficiency.code, proficiency.display_name, proficiency.rank
            FROM benchmark.version AS version
            JOIN taxonomy.proficiency AS proficiency ON proficiency.taxonomy_version_id = version.taxonomy_version_id
            WHERE version.benchmark_version_id = $1
            ORDER BY proficiency.rank
            """,
            connection);
        command.Parameters.AddWithValue(benchmarkVersionId);
        var scale = new List<AuthorizedLevel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            scale.Add(new AuthorizedLevel(reader.GetInt32(2), reader.GetString(0), reader.GetString(1)));
        if (scale.Count == 0)
            throw new InvalidOperationException("That benchmark version has no proficiency scale.");
        return scale;
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
