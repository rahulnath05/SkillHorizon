using SkillHorizon.Hosting;
using SkillHorizon.Readiness;

namespace SkillHorizon.Worker;

public sealed class PracticeProjectionWorker : BackgroundService
{
    private readonly IPracticeProjection _projection;
    private readonly ILogger<PracticeProjectionWorker> _logger;
    private readonly SkillHorizonOptions _options;

    public PracticeProjectionWorker(
        IPracticeProjection projection,
        ILogger<PracticeProjectionWorker> logger,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options)
    {
        _projection = projection;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ProjectPracticeOnStartup)
            await Project(stoppingToken).ConfigureAwait(false);
        if (_options.PracticeProjectionIntervalSeconds <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PracticeProjectionIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await Project(stoppingToken).ConfigureAwait(false);
    }

    private async Task Project(CancellationToken cancellationToken)
    {
        try
        {
            await _projection.ProjectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Practice projection failed. zone={Zone}", _options.ZoneId);
        }
    }
}
