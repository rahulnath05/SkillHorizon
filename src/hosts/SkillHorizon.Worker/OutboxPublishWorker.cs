using SkillHorizon.Hosting;
using SkillHorizon.Messaging;

namespace SkillHorizon.Worker;

public sealed class OutboxPublishWorker : BackgroundService
{
    private readonly IOutboxPublisher _publisher;
    private readonly ILogger<OutboxPublishWorker> _logger;
    private readonly SkillHorizonOptions _options;

    public OutboxPublishWorker(
        IOutboxPublisher publisher,
        ILogger<OutboxPublishWorker> logger,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options)
    {
        _publisher = publisher;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PublishOutbox)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await _publisher.PublishPendingAsync(stoppingToken).ConfigureAwait(false);
                var delay = published > 0 ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(2);
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox publish failed. zone={Zone}", _options.ZoneId);
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
