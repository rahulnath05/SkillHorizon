using SkillHorizon.Hosting;
using SkillHorizon.Messaging;

namespace SkillHorizon.Worker;

public sealed class ReferenceFanoutWorker : BackgroundService
{
    private readonly ReferenceDistributor _distributor;
    private readonly IZoneDirectory _directory;
    private readonly ILogger<ReferenceFanoutWorker> _logger;
    private readonly SkillHorizonOptions _options;

    public ReferenceFanoutWorker(
        ReferenceDistributor distributor,
        IZoneDirectory directory,
        ILogger<ReferenceFanoutWorker> logger,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options)
    {
        _distributor = distributor;
        _directory = directory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.FanoutReference)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var subscribers = await _directory.ReferenceSubscribersAsync(stoppingToken).ConfigureAwait(false);
                var destinations = subscribers
                    .Select(subscriber => new ReferenceDestination(subscriber.ZoneId, subscriber.QueueUrl))
                    .ToArray();
                var delivered = await _distributor.DeliverAsync(destinations, stoppingToken).ConfigureAwait(false);
                if (delivered > 0)
                {
                    _logger.LogInformation(
                        "Reference fan-out delivered {Count} publication(s). zone={Zone}",
                        delivered,
                        _options.ZoneId);
                }

                await Task.Delay(delivered > 0 ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reference fan-out failed. zone={Zone}", _options.ZoneId);
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
