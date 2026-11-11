using SkillHorizon.Hosting;
using SkillHorizon.Workforce;

namespace SkillHorizon.Worker;

public sealed class TeamProjectionWorker : BackgroundService
{
    private readonly ITeamWorkspace _workspace;
    private readonly ILogger<TeamProjectionWorker> _logger;
    private readonly SkillHorizonOptions _options;

    public TeamProjectionWorker(
        ITeamWorkspace workspace,
        ILogger<TeamProjectionWorker> logger,
        Microsoft.Extensions.Options.IOptions<SkillHorizonOptions> options)
    {
        _workspace = workspace;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ProjectTeamOnStartup)
            await Project(stoppingToken).ConfigureAwait(false);
        if (_options.TeamProjectionIntervalSeconds <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.TeamProjectionIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await Project(stoppingToken).ConfigureAwait(false);
    }

    private async Task Project(CancellationToken cancellationToken)
    {
        try
        {
            await _workspace.ProjectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Team projection failed. zone={Zone}", _options.ZoneId);
        }
    }
}
