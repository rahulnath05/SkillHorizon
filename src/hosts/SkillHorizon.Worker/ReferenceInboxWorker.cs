using SkillHorizon.Benchmark;
using SkillHorizon.Hosting;
using SkillHorizon.Messaging;

namespace SkillHorizon.Worker;

public sealed class ReferenceInboxWorker : BackgroundService
{
    private readonly ReferenceDistributor _distributor;
    private readonly IReferenceLibrary _library;
    private readonly ILogger<ReferenceInboxWorker> _logger;
    private readonly SkillHorizonOptions _options;

    public ReferenceInboxWorker(
        ReferenceDistributor distributor,
        IReferenceLibrary library,
        ILogger<ReferenceInboxWorker> logger,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options)
    {
        _distributor = distributor;
        _library = library;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ReceiveReference)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var received = await _distributor.ReceiveAsync(
                    (id, type, payload, cancellationToken) => _library.ApplyPublicationAsync(id, type, payload, cancellationToken),
                    stoppingToken).ConfigureAwait(false);
                if (received > 0)
                {
                    _logger.LogInformation(
                        "Reference inbox applied {Count} publication(s). zone={Zone}",
                        received,
                        _options.ZoneId);
                }

                await Task.Delay(received > 0 ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reference inbox failed. zone={Zone}", _options.ZoneId);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
