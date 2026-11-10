using SkillHorizon.Hosting;
using SkillHorizon.Workforce;

namespace SkillHorizon.Worker;

public sealed class WorkforceSyncWorker : BackgroundService
{
    private readonly IWorkforceSync _sync;
    private readonly ILogger<WorkforceSyncWorker> _logger;
    private readonly SkillHorizonOptions _options;

    public WorkforceSyncWorker(
        IWorkforceSync sync,
        ILogger<WorkforceSyncWorker> logger,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options)
    {
        _sync = sync;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.SyncWorkforceOnStartup)
            return;

        try
        {
            var result = await _sync.SyncAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Workforce sync completed. zone={Zone} projected={Projected} quarantined={Quarantined} closed={Closed}",
                _options.ZoneId,
                result.Projected,
                result.Quarantined,
                result.Closed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Workforce sync failed. zone={Zone}", _options.ZoneId);
        }
    }
}
