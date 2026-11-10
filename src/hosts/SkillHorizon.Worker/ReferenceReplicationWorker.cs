using SkillHorizon.Benchmark;
using SkillHorizon.Hosting;

namespace SkillHorizon.Worker;

public sealed class ReferenceReplicationWorker : BackgroundService
{
    private readonly IReferenceLibrary _reference;
    private readonly ILogger<ReferenceReplicationWorker> _logger;
    private readonly SkillHorizonOptions _options;

    public ReferenceReplicationWorker(
        IReferenceLibrary reference,
        ILogger<ReferenceReplicationWorker> logger,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options)
    {
        _reference = reference;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ReplicateReference)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var applied = await _reference.ApplyPendingAsync(stoppingToken).ConfigureAwait(false);
                if (applied > 0)
                {
                    _logger.LogInformation(
                        "Reference replication applied {Count} publication(s). zone={Zone}",
                        applied,
                        _options.ZoneId);
                }

                var delay = applied > 0 ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(1);
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reference replication failed. zone={Zone}", _options.ZoneId);
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
