using System.Net.Http.Headers;
using Npgsql;

namespace SkillHorizon.Learning;

public sealed record BankItem(Guid ItemId, string SkillCode, int Difficulty, string Prompt, string? Rubric);

public sealed record BankMark(Guid ItemId, string SkillCode, int Band, string ResponseBody, DateTimeOffset MarkedAt);

public sealed record PanelExpert(Guid SmeId, string DomainName, string ExpertName, string Credentials);

public sealed class AssessmentBank
{
    private readonly NpgsqlDataSource _assessment;
    private readonly HttpClient _core;

    public AssessmentBank(NpgsqlDataSource assessment, HttpClient core)
    {
        _assessment = assessment;
        _core = core;
    }

    public async Task<IReadOnlyList<BankItem>> ListAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT item.item_id, item.skill_code, item.difficulty, item.prompt, rubric.body
            FROM assessment.item AS item
            LEFT JOIN assessment.rubric AS rubric ON rubric.item_id = item.item_id
            ORDER BY item.skill_code, item.difficulty, item.prompt
            """,
            connection);
        var items = new List<BankItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new BankItem(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return items;
    }

    public async Task<BankItem> WriteAsync(
        string bearer,
        string workspace,
        string correlationId,
        string? skillCode,
        string? prompt,
        int difficulty,
        string? rubric,
        IReadOnlyList<(int Rank, string Label)>? choices,
        CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        var skill = skillCode?.Trim() ?? "";
        var stem = prompt?.Trim() ?? "";
        var bands = string.IsNullOrWhiteSpace(rubric) ? null : rubric.Trim();
        if (skill.Length == 0 || stem.Length == 0)
            throw new InvalidOperationException("An item needs a skill and a prompt.");
        if (difficulty is < 1 or > 5)
            throw new InvalidOperationException("Difficulty is a whole number from 1 to 5.");

        var options = (choices ?? [])
            .Select(choice => (choice.Rank, Label: choice.Label.Trim()))
            .Where(choice => choice.Label.Length > 0)
            .ToArray();
        if (bands is null && options.Length < 2)
            throw new InvalidOperationException("A selected-answer item needs at least two choices.");
        if (bands is null && (options.Any(choice => choice.Rank < 1) || options.Select(choice => choice.Rank).Distinct().Count() != options.Length))
            throw new InvalidOperationException("Each choice needs its own positive rank.");
        if (bands is not null && options.Length > 0)
            throw new InvalidOperationException("A typed item is marked from its rubric and has no choices.");

        var itemId = Guid.NewGuid();
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO assessment.item (item_id, skill_code, prompt, difficulty)
            VALUES ($1, $2, $3, $4)
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(itemId);
            insert.Parameters.AddWithValue(skill);
            insert.Parameters.AddWithValue(stem);
            insert.Parameters.AddWithValue(difficulty);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (bands is not null)
        {
            await using var store = new NpgsqlCommand(
                "INSERT INTO assessment.rubric (item_id, body) VALUES ($1, $2)",
                connection,
                transaction);
            store.Parameters.AddWithValue(itemId);
            store.Parameters.AddWithValue(bands);
            await store.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var choice in options)
            {
                await using var store = new NpgsqlCommand(
                    "INSERT INTO assessment.choice (item_id, rank, label) VALUES ($1, $2, $3)",
                    connection,
                    transaction);
                store.Parameters.AddWithValue(itemId);
                store.Parameters.AddWithValue(choice.Rank);
                store.Parameters.AddWithValue(choice.Label);
                await store.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BankItem(itemId, skill, difficulty, stem, bands);
    }

    public async Task<IReadOnlyList<BankMark>> MarksAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT mark.item_id, item.skill_code, mark.band, mark.response_body, mark.marked_at
            FROM assessment.mark AS mark
            JOIN assessment.item AS item ON item.item_id = mark.item_id
            ORDER BY mark.marked_at DESC
            """,
            connection);
        var marks = new List<BankMark>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            marks.Add(new BankMark(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return marks;
    }

    public async Task<IReadOnlyList<PanelExpert>> PanelAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        return await ReadPanel(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PanelExpert> NameExpertAsync(
        string bearer,
        string workspace,
        string correlationId,
        string? domainName,
        string? expertName,
        string? credentials,
        CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        var domain = domainName?.Trim() ?? "";
        var expert = expertName?.Trim() ?? "";
        var statement = credentials?.Trim() ?? "";
        if (domain.Length == 0 || expert.Length == 0 || statement.Length == 0)
            throw new InvalidOperationException("A panel row names a domain, an expert, and their credentials.");

        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO assessment.sme (sme_id, domain_name, expert_name, credentials)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (domain_name) DO UPDATE SET
                expert_name = EXCLUDED.expert_name,
                credentials = EXCLUDED.credentials
            RETURNING sme_id
            """,
            connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(domain);
        command.Parameters.AddWithValue(expert);
        command.Parameters.AddWithValue(statement);
        var smeId = (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        return new PanelExpert(smeId, domain, expert, statement);
    }

    private async Task<IReadOnlyList<PanelExpert>> ReadPanel(CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT sme_id, domain_name, expert_name, credentials
            FROM assessment.sme
            ORDER BY domain_name
            """,
            connection);
        var panel = new List<PanelExpert>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            panel.Add(new PanelExpert(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return panel;
    }

    private async Task RequireAuthor(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "studio/author");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("X-Active-Workspace", workspace);
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        using var response = await _core.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("This command is refused for the active workspace.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The core API refused the assessment studio.");
    }
}
