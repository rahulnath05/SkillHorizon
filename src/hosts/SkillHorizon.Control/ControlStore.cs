using Npgsql;

namespace SkillHorizon.Control;

public sealed record ZoneRecord(string ZoneId, string DisplayName, string Status, string ApiBaseUrl);

public sealed record LegalEntityRoute(string LegalEntityId, string ZoneId, string Status, string DisplayName);

public sealed record RegisterZone(string ZoneId, string DisplayName, string Status, string ApiBaseUrl);

public sealed record RegisterLegalEntity(string LegalEntityId, string ZoneId, string DisplayName);

public sealed class ControlStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ControlStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<ZoneRecord>> ListZonesAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT zone_id, display_name, status, api_base_url
            FROM zone.zone
            ORDER BY zone_id
            """);
        var zones = new List<ZoneRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            zones.Add(new ZoneRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return zones;
    }

    public async Task<LegalEntityRoute?> FindLegalEntityAsync(string legalEntityId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT entity.legal_entity_id, entity.zone_id, zone.status, entity.display_name
            FROM routing.legal_entity AS entity
            JOIN zone.zone AS zone ON zone.zone_id = entity.zone_id
            WHERE entity.legal_entity_id = $1
            """);
        command.Parameters.AddWithValue(legalEntityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new LegalEntityRoute(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    public async Task<bool> RegisterZoneAsync(RegisterZone zone, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO zone.zone (zone_id, display_name, status, api_base_url, registered_at)
            VALUES ($1, $2, $3, $4, now())
            ON CONFLICT (zone_id) DO NOTHING
            """);
        command.Parameters.AddWithValue(zone.ZoneId);
        command.Parameters.AddWithValue(zone.DisplayName);
        command.Parameters.AddWithValue(zone.Status);
        command.Parameters.AddWithValue(zone.ApiBaseUrl);
        var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return written == 1;
    }

    public async Task<bool> RegisterLegalEntityAsync(RegisterLegalEntity entity, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO routing.legal_entity (legal_entity_id, zone_id, display_name)
            VALUES ($1, $2, $3)
            ON CONFLICT (legal_entity_id) DO NOTHING
            """);
        command.Parameters.AddWithValue(entity.LegalEntityId);
        command.Parameters.AddWithValue(entity.ZoneId);
        command.Parameters.AddWithValue(entity.DisplayName);
        var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return written == 1;
    }
}
