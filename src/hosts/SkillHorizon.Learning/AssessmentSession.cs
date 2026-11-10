using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SkillHorizon.Hosting;

namespace SkillHorizon.Learning;

public sealed record SittingItem(Guid ItemId, string SkillCode, string Prompt, IReadOnlyList<SittingChoice> Choices);

public sealed record SittingChoice(int Rank, string Label);

public sealed record OpenSitting(Guid SittingId, SittingItem? Item, bool ReadyToSeal);

public sealed record SealedSitting(Guid ResultId, IReadOnlyList<SkillGapView> Gap);

public sealed record SkillGapView(string SkillName, string CurrentName, int CurrentRank, string TargetName, int TargetRank);

public sealed class AssessmentSession
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _assessment;
    private readonly HttpClient _core;
    private readonly ProgressSteps _progress;
    private readonly string _learningKey;

    public AssessmentSession(NpgsqlDataSource assessment, HttpClient core, ProgressSteps progress, string learningKey)
    {
        _assessment = assessment;
        _core = core;
        _progress = progress;
        _learningKey = learningKey;
    }

    private async Task RequireOpen(Guid sittingId, CoreGrant grant, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT status FROM assessment.sitting
            WHERE sitting_id = $1 AND subject_id = $2 AND wave_id = $3
            """,
            connection);
        command.Parameters.AddWithValue(sittingId);
        command.Parameters.AddWithValue(grant.SubjectId);
        command.Parameters.AddWithValue(grant.WaveId);
        var status = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (status != "open")
            throw new InvalidOperationException("That sitting is not open.");
    }

    public async Task<OpenSitting> OpenAsync(string token, CancellationToken cancellationToken)
    {
        var grant = await Grant(token, cancellationToken).ConfigureAwait(false);
        var sittingId = await ResumeOrCreate(grant, token, cancellationToken).ConfigureAwait(false);
        return await Next(sittingId, grant.Skills.Select(skill => skill.SkillCode).ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpenSitting> AnswerAsync(Guid sittingId, string token, Guid itemId, int rank, CancellationToken cancellationToken)
    {
        var grant = await Grant(token, cancellationToken).ConfigureAwait(false);
        await RequireOpen(sittingId, grant, cancellationToken).ConfigureAwait(false);
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var skillCode = await SkillForItem(connection, transaction, itemId, cancellationToken).ConfigureAwait(false);
        if (!grant.Skills.Any(skill => skill.SkillCode == skillCode))
            throw new InvalidOperationException("That item is not on this sitting.");

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO assessment.response (sitting_id, item_id, skill_code, selected_rank, responded_at)
            VALUES ($1, $2, $3, $4, now())
            ON CONFLICT (sitting_id, item_id) DO UPDATE SET selected_rank = EXCLUDED.selected_rank, responded_at = now()
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue(sittingId);
        insert.Parameters.AddWithValue(itemId);
        insert.Parameters.AddWithValue(skillCode);
        insert.Parameters.AddWithValue(rank);
        var updated = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated == 0)
            throw new InvalidOperationException("That sitting is not open.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await Next(sittingId, grant.Skills.Select(skill => skill.SkillCode).ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<SealedSitting> SealAsync(Guid sittingId, string token, string correlationId, CancellationToken cancellationToken)
    {
        var sitting = await Find(sittingId, token, cancellationToken).ConfigureAwait(false);
        var existing = await ExistingResult(sittingId, cancellationToken).ConfigureAwait(false);
        if (existing is Guid already)
            return await Publish(sitting, already, correlationId, cancellationToken).ConfigureAwait(false);

        var grant = await Grant(token, cancellationToken).ConfigureAwait(false);
        if (grant.AuthorizationId != sitting.AuthorizationId)
            throw new InvalidOperationException("That sitting token is not active.");

        var answers = await Answers(sittingId, cancellationToken).ConfigureAwait(false);
        if (grant.Skills.Any(skill => !answers.ContainsKey(skill.SkillCode)))
            throw new InvalidOperationException("Every item needs a response before the result can be sealed.");

        var resultId = Guid.NewGuid();
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var result = new NpgsqlCommand(
            """
            INSERT INTO assessment.sealed_result (result_id, sitting_id, subject_id, benchmark_version_id, sealed_at)
            VALUES ($1, $2, $3, $4, now())
            """,
            connection,
            transaction))
        {
            result.Parameters.AddWithValue(resultId);
            result.Parameters.AddWithValue(sittingId);
            result.Parameters.AddWithValue(sitting.SubjectId);
            result.Parameters.AddWithValue(sitting.BenchmarkVersionId);
            await result.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var skill in grant.Skills)
        {
            var rank = answers[skill.SkillCode];
            var level = grant.Scale.SingleOrDefault(item => item.Rank == rank)
                ?? throw new InvalidOperationException("A response must use a proficiency on the benchmark scale.");
            await using var proficiency = new NpgsqlCommand(
                """
                INSERT INTO assessment.sealed_proficiency (
                    result_id, skill_code, skill_name, proficiency_rank, proficiency_code, proficiency_name)
                VALUES ($1, $2, $3, $4, $5, $6)
                """,
                connection,
                transaction);
            proficiency.Parameters.AddWithValue(resultId);
            proficiency.Parameters.AddWithValue(skill.SkillCode);
            proficiency.Parameters.AddWithValue(skill.SkillName);
            proficiency.Parameters.AddWithValue(level.Rank);
            proficiency.Parameters.AddWithValue(level.Code);
            proficiency.Parameters.AddWithValue(level.Name);
            await proficiency.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var close = new NpgsqlCommand(
            "UPDATE assessment.sitting SET status = 'sealed', sealed_at = now() WHERE sitting_id = $1 AND status = 'open'",
            connection,
            transaction))
        {
            close.Parameters.AddWithValue(sittingId);
            if (await close.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                throw new InvalidOperationException("That sitting is not open.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await Publish(
            new OwnedSitting(grant.AuthorizationId, grant.SubjectId, grant.BenchmarkVersionId),
            resultId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SealedSitting> Publish(OwnedSitting grant, Guid resultId, string correlationId, CancellationToken cancellationToken)
    {
        var proficiencies = await Proficiencies(resultId, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/results");
        request.Headers.TryAddWithoutValidation(LearningServiceGate.Header, _learningKey);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation(SkillHorizonContext.CorrelationHeader, correlationId);
        request.Content = JsonContent.Create(new
        {
            authorizationId = grant.AuthorizationId,
            resultId,
            proficiencies
        });
        using var response = await _core.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The core API refused the sealed result.");
        await _progress.CompleteAssessmentAsync(grant.SubjectId, grant.BenchmarkVersionId, resultId, cancellationToken).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<GapDocument>(body, Json) ?? new GapDocument([]);
        return new SealedSitting(resultId, document.Gap ?? []);
    }

    private async Task<CoreGrant> Grant(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "internal/sittings/authorizations");
        request.Headers.TryAddWithoutValidation(LearningServiceGate.Header, _learningKey);
        request.Headers.TryAddWithoutValidation("X-Sitting-Token", token);
        using var response = await _core.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("That sitting token is not active.");
        return await response.Content.ReadFromJsonAsync<CoreGrant>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("That sitting token is not active.");
    }

    private async Task<Guid> ResumeOrCreate(CoreGrant grant, string token, CancellationToken cancellationToken)
    {
        var hash = TokenHash(token);
        var scale = JsonSerializer.Serialize(grant.Scale, Json);
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO assessment.sitting (
                sitting_id, authorization_id, token_hash, subject_id, wave_id, benchmark_version_id, scale, status, opened_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb, 'open', now())
            ON CONFLICT (subject_id, wave_id) DO UPDATE
                SET token_hash = EXCLUDED.token_hash,
                    authorization_id = EXCLUDED.authorization_id
                WHERE assessment.sitting.status = 'open'
            RETURNING sitting_id
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(grant.AuthorizationId);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(grant.SubjectId);
        command.Parameters.AddWithValue(grant.WaveId);
        command.Parameters.AddWithValue(grant.BenchmarkVersionId);
        command.Parameters.AddWithValue(scale);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is not Guid sittingId)
            throw new InvalidOperationException("This wave already has a sealed result.");
        return sittingId;
    }

    private async Task<OpenSitting> Next(Guid sittingId, IReadOnlyList<string> skillCodes, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT item.item_id, item.skill_code, item.prompt
            FROM assessment.item AS item
            WHERE item.skill_code = ANY ($2)
              AND NOT EXISTS (
                  SELECT 1 FROM assessment.response AS response
                  WHERE response.sitting_id = $1 AND response.item_id = item.item_id)
            ORDER BY item.skill_code
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue(sittingId);
        command.Parameters.Add(new NpgsqlParameter { Value = skillCodes.ToArray(), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text });
        Guid itemId;
        string skillCode;
        string prompt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return new OpenSitting(sittingId, null, true);
            itemId = reader.GetGuid(0);
            skillCode = reader.GetString(1);
            prompt = reader.GetString(2);
        }

        var choices = new List<SittingChoice>();
        await using var choiceCommand = new NpgsqlCommand(
            "SELECT rank, label FROM assessment.choice WHERE item_id = $1 ORDER BY rank",
            connection);
        choiceCommand.Parameters.AddWithValue(itemId);
        await using var choiceReader = await choiceCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await choiceReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            choices.Add(new SittingChoice(choiceReader.GetInt32(0), choiceReader.GetString(1)));
        return new OpenSitting(sittingId, new SittingItem(itemId, skillCode, prompt, choices), false);
    }

    private static async Task<string> SkillForItem(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid itemId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT skill_code FROM assessment.item WHERE item_id = $1", connection, transaction);
        command.Parameters.AddWithValue(itemId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found as string ?? throw new InvalidOperationException("That item does not exist.");
    }

    private async Task<OwnedSitting> Find(Guid sittingId, string token, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT authorization_id, subject_id, benchmark_version_id
            FROM assessment.sitting
            WHERE sitting_id = $1 AND token_hash = $2
            """,
            connection);
        command.Parameters.AddWithValue(sittingId);
        command.Parameters.AddWithValue(TokenHash(token));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("That sitting token is not active.");
        return new OwnedSitting(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2));
    }

    private async Task<Guid?> ExistingResult(Guid sittingId, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT result_id FROM assessment.sealed_result WHERE sitting_id = $1",
            connection);
        command.Parameters.AddWithValue(sittingId);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is Guid resultId ? resultId : null;
    }

    private async Task<Dictionary<string, int>> Answers(Guid sittingId, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT skill_code, selected_rank FROM assessment.response WHERE sitting_id = $1",
            connection);
        command.Parameters.AddWithValue(sittingId);
        var answers = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            answers[reader.GetString(0)] = reader.GetInt32(1);
        return answers;
    }

    private async Task<MeasuredView[]> Proficiencies(Guid resultId, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT skill_code, skill_name, proficiency_rank, proficiency_code, proficiency_name
            FROM assessment.sealed_proficiency
            WHERE result_id = $1
            ORDER BY skill_name
            """,
            connection);
        command.Parameters.AddWithValue(resultId);
        var skills = new List<MeasuredView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            skills.Add(new MeasuredView(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4)));
        return skills.ToArray();
    }

    private static string TokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed record OwnedSitting(Guid AuthorizationId, string SubjectId, Guid BenchmarkVersionId);

    private sealed record CoreSkill(Guid SkillId, string SkillCode, string SkillName, int TargetRank, string TargetCode, string TargetName);

    private sealed record CoreLevel(int Rank, string Code, string Name);

    private sealed record CoreGrant(
        Guid AuthorizationId,
        string SubjectId,
        Guid WaveId,
        Guid BenchmarkVersionId,
        CoreSkill[] Skills,
        CoreLevel[] Scale);

    private sealed record MeasuredView(string SkillCode, string SkillName, int Rank, string Code, string Name);

    private sealed record GapDocument(SkillGapView[]? Gap);
}
