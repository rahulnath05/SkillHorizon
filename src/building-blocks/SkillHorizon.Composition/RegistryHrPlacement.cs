using Microsoft.Extensions.Options;
using SkillHorizon.Hosting;
using SkillHorizon.Workforce;

namespace SkillHorizon.Composition;

public sealed class RegistryHrPlacement : IHrPlacement
{
    private readonly IZoneDirectory _directory;
    private readonly SkillHorizonOptions _options;

    public RegistryHrPlacement(IZoneDirectory directory, IOptions<SkillHorizonOptions> options)
    {
        _directory = directory;
        _options = options.Value;
    }

    public async Task<bool> AcceptsAsync(string legalEntityId, CancellationToken cancellationToken)
    {
        if (!await _directory.HrFeedEnabledAsync(_options.ZoneId, cancellationToken).ConfigureAwait(false))
            return false;

        var route = await _directory.FindByLegalEntityAsync(legalEntityId, cancellationToken).ConfigureAwait(false);
        return route is not null
            && string.Equals(route.Status, "active", StringComparison.Ordinal)
            && string.Equals(route.ZoneId, _options.ZoneId, StringComparison.Ordinal);
    }
}
