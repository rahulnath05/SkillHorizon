namespace SkillHorizon.Hosting;

public sealed record ZoneRoute(string LegalEntityId, string ZoneId, string Status);

public interface IZoneDirectory
{
    Task<ZoneRoute?> FindByLegalEntityAsync(string legalEntityId, CancellationToken cancellationToken);
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
