using Microsoft.Extensions.Options;
using SkillHorizon.Activation;
using SkillHorizon.Benchmark;
using SkillHorizon.Hosting;

namespace SkillHorizon.Api;

public sealed class LearningAdmissionFixture
{
    private readonly IWaveAdmission _waves;
    private readonly IBenchmarkAuthoring _benchmarks;
    private readonly SkillHorizonOptions _options;

    public LearningAdmissionFixture(IWaveAdmission waves, IBenchmarkAuthoring benchmarks, IOptions<SkillHorizonOptions> options)
    {
        _waves = waves;
        _benchmarks = benchmarks;
        _options = options.Value;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _waves.EnsureBudgetAsync(_options.ZoneId, _options.ZoneSittingBudget, cancellationToken).ConfigureAwait(false);
        if (!_options.LoadLearningFixture)
            return;

        var published = await _benchmarks.PublishedAsync(cancellationToken).ConfigureAwait(false);
        var current = published
            .Where(version => version.Status == "published" && version.ProfileCode == "data-analyst")
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();
        if (current is null)
            return;

        var existing = await _waves.CurrentWaveAsync("person-single", cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return;

        await _waves.AdmitAsync(
            _options.ZoneId,
            current.BenchmarkVersionId,
            ["person-single"],
            "person-ld",
            "learning-fixture",
            cancellationToken).ConfigureAwait(false);
    }
}
