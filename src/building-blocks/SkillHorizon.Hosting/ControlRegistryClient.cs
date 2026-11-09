using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SkillHorizon.Hosting;

public sealed class ControlRegistryClient : IZoneDirectory
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ISecretStore _secrets;
    private readonly SkillHorizonOptions _options;

    public ControlRegistryClient(
        HttpClient http,
        IMemoryCache cache,
        ISecretStore secrets,
        IOptions<SkillHorizonOptions> options)
    {
        _http = http;
        _cache = cache;
        _secrets = secrets;
        _options = options.Value;
    }

    public async Task<ZoneRoute?> FindByLegalEntityAsync(string legalEntityId, CancellationToken cancellationToken)
    {
        var key = "legal-entity:" + legalEntityId;
        if (_cache.TryGetValue(key, out ZoneRoute? cached) && cached is not null)
            return cached;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "routing/legal-entities/" + Uri.EscapeDataString(legalEntityId));
            var registryKey = await _secrets.GetRequiredAsync(_options.RegistryKeySecretName, cancellationToken)
                .ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("X-Registry-Key", registryKey);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                throw new ZoneDirectoryUnavailableException(
                    $"The control registry returned {(int)response.StatusCode}.");
            }

            var route = await response.Content.ReadFromJsonAsync<ZoneRoute>(cancellationToken).ConfigureAwait(false);
            if (route is null)
                throw new ZoneDirectoryUnavailableException("The control registry returned an empty route.");

            _cache.Set(key, route, TimeSpan.FromSeconds(60));
            return route;
        }
        catch (ZoneDirectoryUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ZoneDirectoryUnavailableException("The control registry is unavailable.", exception);
        }
    }
}
