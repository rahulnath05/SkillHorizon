using Npgsql;

namespace SkillHorizon.Learning;

public sealed class ItemFixture
{
    private readonly NpgsqlDataSource _assessment;

    public ItemFixture(NpgsqlDataSource assessment)
    {
        _assessment = assessment;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await Save("sql", "Which statement describes the SQL you use on this profile?", cancellationToken).ConfigureAwait(false);
        await Save("modeling", "Which statement describes the data models you build?", cancellationToken).ConfigureAwait(false);
    }

    private async Task Save(string skillCode, string prompt, CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var exists = new NpgsqlCommand("SELECT item_id FROM assessment.item WHERE skill_code = $1", connection);
        exists.Parameters.AddWithValue(skillCode);
        if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is Guid)
            return;

        var itemId = Guid.NewGuid();
        await using var insert = new NpgsqlCommand(
            "INSERT INTO assessment.item (item_id, skill_code, prompt) VALUES ($1, $2, $3)",
            connection);
        insert.Parameters.AddWithValue(itemId);
        insert.Parameters.AddWithValue(skillCode);
        insert.Parameters.AddWithValue(prompt);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var choices = new (int Rank, string Label)[]
        {
            (1, "Beginner"),
            (2, "Intermediate"),
            (3, "Advanced"),
            (4, "Expert")
        };
        foreach (var (rank, label) in choices)
        {
            await using var choice = new NpgsqlCommand(
                "INSERT INTO assessment.choice (item_id, rank, label) VALUES ($1, $2, $3)",
                connection);
            choice.Parameters.AddWithValue(itemId);
            choice.Parameters.AddWithValue(rank);
            choice.Parameters.AddWithValue(label);
            await choice.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
