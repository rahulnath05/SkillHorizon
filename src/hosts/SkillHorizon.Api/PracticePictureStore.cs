using Npgsql;

namespace SkillHorizon.Api;

public sealed record HeatmapCellView(
    string SkillCode,
    string SkillName,
    string LevelCode,
    string LevelName,
    int LevelRank,
    int ProficiencyRank,
    string ProficiencyName);

public sealed record HeatmapView(DateTimeOffset? AsOf, IReadOnlyList<HeatmapCellView> Cells);

public sealed record ProgressionView(DateTimeOffset? AsOf, int PathCount, int StepCount, int OpenStepCount);

public sealed record ReadinessCutView(string Key, int MemberCount, int MeasuredCount, int GapCount, DateTimeOffset AsOf);

public sealed class PracticePictureStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PracticePictureStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HeatmapView> ReadHeatmapAsync(string practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? asOf = null;
        await using (var header = new NpgsqlCommand(
            "SELECT as_of FROM readiness.heatmap WHERE practice_code = $1",
            connection))
        {
            header.Parameters.AddWithValue(practiceCode);
            var value = await header.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is not null)
                asOf = Timestamp(value);
        }

        var cells = new List<HeatmapCellView>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT skill_code, skill_name, level_code, level_name, level_rank, proficiency_rank, proficiency_name
            FROM readiness.heatmap_cell
            WHERE practice_code = $1
            ORDER BY level_rank, skill_name
            """,
            connection))
        {
            command.Parameters.AddWithValue(practiceCode);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cells.Add(new HeatmapCellView(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetString(6)));
            }
        }

        return new HeatmapView(asOf, cells);
    }

    public async Task<ProgressionView> ReadProgressionAsync(string practiceCode, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT as_of, path_count, step_count, open_step_count
            FROM readiness.progression
            WHERE practice_code = $1
            """,
            connection);
        command.Parameters.AddWithValue(practiceCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new ProgressionView(null, 0, 0, 0);
        return new ProgressionView(Timestamp(reader.GetValue(0)), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    public async Task<IReadOnlyList<ReadinessCutView>> ReadCutsAsync(string practiceCode, string kind, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var cuts = new List<ReadinessCutView>();
        await using var command = new NpgsqlCommand(
            """
            SELECT cut_key, member_count, measured_count, gap_count, as_of
            FROM readiness.cut
            WHERE practice_code = $1 AND cut_kind = $2
            ORDER BY cut_key
            """,
            connection);
        command.Parameters.AddWithValue(practiceCode);
        command.Parameters.AddWithValue(kind);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cuts.Add(new ReadinessCutView(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                Timestamp(reader.GetValue(4))));
        }

        return cuts;
    }

    private static DateTimeOffset Timestamp(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A timestamp was not returned.")
    };
}
