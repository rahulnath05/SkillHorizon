namespace SkillHorizon.Hosting;

public sealed record ZoneRoute(string LegalEntityId, string ZoneId, string Status);

public sealed record ReferenceSubscription(string ZoneId, string ApiBaseUrl, string QueueUrl);

public sealed record ZoneResidency(string ZoneId, string DisplayName, string Status, IReadOnlyList<ResidencyEntity> LegalEntities);

public sealed record ResidencyEntity(string LegalEntityId, string DisplayName);

public sealed record ConnectorStatus(string ConnectorId, string ConnectorType, bool Enabled, string? Endpoint);

public interface IZoneDirectory
{
    Task<ZoneRoute?> FindByLegalEntityAsync(string legalEntityId, CancellationToken cancellationToken);

    Task<bool> HrFeedEnabledAsync(string zoneId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceSubscription>> ReferenceSubscribersAsync(CancellationToken cancellationToken);

    Task<ZoneResidency?> ReadResidencyAsync(string zoneId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConnectorStatus>> ListConnectorsAsync(string zoneId, CancellationToken cancellationToken);
}

public sealed class ZoneDirectoryUnavailableException : Exception
{
    public ZoneDirectoryUnavailableException(string message) : base(message)
    {
    }

    public ZoneDirectoryUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
