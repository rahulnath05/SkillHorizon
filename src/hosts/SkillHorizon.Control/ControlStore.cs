using Npgsql;

namespace SkillHorizon.Control;

public sealed record ZoneRecord(string ZoneId, string DisplayName, string Status, string ApiBaseUrl, string QueueUrl);

public sealed record LegalEntityRoute(string LegalEntityId, string ZoneId, string Status, string DisplayName);

public sealed record RegisterZone(string ZoneId, string DisplayName, string Status, string ApiBaseUrl, string? QueueUrl = null);

public sealed record RegisterLegalEntity(string LegalEntityId, string ZoneId, string DisplayName);

public sealed record ZoneSubscriber(string ZoneId, string ApiBaseUrl, string QueueUrl);

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
            SELECT zone_id, display_name, status, api_base_url, queue_url
            FROM zone.zone
            ORDER BY zone_id
            """);
        var zones = new List<ZoneRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            zones.Add(new ZoneRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
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
            INSERT INTO zone.zone (zone_id, display_name, status, api_base_url, queue_url, registered_at)
            VALUES ($1, $2, $3, $4, $5, now())
            ON CONFLICT (zone_id) DO NOTHING
            """);
        command.Parameters.AddWithValue(zone.ZoneId);
        command.Parameters.AddWithValue(zone.DisplayName);
        command.Parameters.AddWithValue(zone.Status);
        command.Parameters.AddWithValue(zone.ApiBaseUrl);
        command.Parameters.AddWithValue(zone.QueueUrl?.Trim() ?? "");
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

    public async Task<bool> SetReferenceSubscriptionAsync(string zoneId, bool enabled, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO capability.flag (zone_id, capability_code, enabled)
            VALUES ($1, 'reference_replication', $2)
            ON CONFLICT (zone_id, capability_code) DO UPDATE SET enabled = EXCLUDED.enabled
            """);
        command.Parameters.AddWithValue(zoneId);
        command.Parameters.AddWithValue(enabled);
        var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return written == 1;
    }

    public async Task<bool> SetHrFeedAsync(string zoneId, bool enabled, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO integration.connector (connector_id, zone_id, connector_type, endpoint, secret_name, enabled)
            VALUES ($1, $2, 'hr', NULL, NULL, $3)
            ON CONFLICT (connector_id) DO UPDATE SET enabled = EXCLUDED.enabled
            """);
        command.Parameters.AddWithValue(zoneId + "-hr");
        command.Parameters.AddWithValue(zoneId);
        command.Parameters.AddWithValue(enabled);
        var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return written == 1;
    }

    public async Task<bool> HrFeedEnabledAsync(string zoneId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT enabled
            FROM integration.connector
            WHERE zone_id = $1 AND connector_type = 'hr'
            """);
        command.Parameters.AddWithValue(zoneId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is bool enabled && enabled;
    }

    public async Task<IReadOnlyList<ZoneSubscriber>> ListReferenceSubscribersAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT zone.zone_id, zone.api_base_url, zone.queue_url
            FROM zone.zone AS zone
            JOIN capability.flag AS flag ON flag.zone_id = zone.zone_id
            WHERE zone.status = 'active'
              AND flag.capability_code = 'reference_replication'
              AND flag.enabled
            ORDER BY zone.zone_id
            """);
        var zones = new List<ZoneSubscriber>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            zones.Add(new ZoneSubscriber(reader.GetString(0), reader.GetString(1), reader.GetString(2)));

        return zones;
    }

    public async Task<bool> ActivateAsync(string zoneId, CancellationToken cancellationToken)
    {
        var reference = false;
        var hr = await HrFeedEnabledAsync(zoneId, cancellationToken).ConfigureAwait(false);
        await using (var command = _dataSource.CreateCommand(
            """
            SELECT enabled
            FROM capability.flag
            WHERE zone_id = $1 AND capability_code = 'reference_replication'
            """))
        {
            command.Parameters.AddWithValue(zoneId);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            reference = value is bool enabled && enabled;
        }

        if (!reference || !hr)
            return false;

        await using var update = _dataSource.CreateCommand(
            """
            UPDATE zone.zone
            SET status = 'active'
            WHERE zone_id = $1 AND status IN ('provisioning', 'active')
            """);
        update.Parameters.AddWithValue(zoneId);
        var written = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return written == 1;
    }
}
