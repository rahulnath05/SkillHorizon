namespace SkillHorizon.Hosting;

public sealed record ZoneRoute(string LegalEntityId, string ZoneId, string Status);

public sealed record ReferenceSubscription(string ZoneId, string ApiBaseUrl, string QueueUrl);

public interface IZoneDirectory
{
    Task<ZoneRoute?> FindByLegalEntityAsync(string legalEntityId, CancellationToken cancellationToken);

    Task<bool> HrFeedEnabledAsync(string zoneId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceSubscription>> ReferenceSubscribersAsync(CancellationToken cancellationToken);
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
