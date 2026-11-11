using Npgsql;
using SkillHorizon.Hosting;

namespace SkillHorizon.Api;

public static class AggregateEndpoints
{
    public static void MapAggregates(this WebApplication app)
    {
        app.MapGet("/aggregates/practice", Read);
    }

    private static async Task<IResult> Read(
        string? practice,
        NpgsqlDataSource dataSource,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(practice))
            return Results.BadRequest();

        await using var command = dataSource.CreateCommand(
            """
            SELECT member_count, measured_count, gap_count, cohort_count, as_of
            FROM readiness.snapshot
            WHERE subject_kind = 'practice' AND subject_key = $1
            """);
        command.Parameters.AddWithValue(practice);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var members = 0;
        var measured = 0;
        var gaps = 0;
        var cohorts = 0;
        DateTimeOffset? asOf = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            members = reader.GetInt32(0);
            measured = reader.GetInt32(1);
            gaps = reader.GetInt32(2);
            cohorts = reader.GetInt32(3);
            asOf = Timestamp(reader.GetValue(4));
        }

        return Results.Ok(new
        {
            zoneId = options.Value.ZoneId,
            practiceCode = practice,
            memberCount = members,
            measuredCount = measured,
            gapCount = gaps,
            cohortCount = cohorts,
            asOf
        });
    }

    private static DateTimeOffset Timestamp(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A snapshot has no as-of time.")
    };
}
