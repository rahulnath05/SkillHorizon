using SkillHorizon.Benchmark;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Taxonomy;

namespace SkillHorizon.Api;

public sealed class StandardsFixture
{
    public const string FrameworkCode = "consulting";

    private readonly IJobArchitecture _jobs;
    private readonly ITaxonomyAuthoring _taxonomy;
    private readonly IBenchmarkAuthoring _benchmarks;

    public StandardsFixture(IJobArchitecture jobs, ITaxonomyAuthoring taxonomy, IBenchmarkAuthoring benchmarks)
    {
        _jobs = jobs;
        _taxonomy = taxonomy;
        _benchmarks = benchmarks;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var existing = await _jobs.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(framework => framework.Code == FrameworkCode))
            return;

        await _jobs.ImportAsync(
            [
                new FrameworkImport(
                    FrameworkCode,
                    "Consulting",
                    [
                        new LevelImport("associate", "Associate", 1),
                        new LevelImport("consultant", "Consultant", 2)
                    ],
                    [new ProfileImport("data-analyst", "Data analyst", "associate")])
            ],
            cancellationToken).ConfigureAwait(false);

        var drafted = await _taxonomy.SaveDraftAsync(
            new TaxonomyDraft(
                [
                    new PracticeDraft(
                        "data",
                        "Data",
                        [
                            new CompetencyDraft(
                                "analysis",
                                "Analysis",
                                [new SkillDraft("sql", "SQL"), new SkillDraft("modeling", "Modeling")])
                        ])
                ],
                [
                    new ProficiencyDraft("beginner", "Beginner", 1),
                    new ProficiencyDraft("intermediate", "Intermediate", 2),
                    new ProficiencyDraft("advanced", "Advanced", 3),
                    new ProficiencyDraft("expert", "Expert", 4)
                ]),
            cancellationToken).ConfigureAwait(false);
        var published = await _taxonomy.PublishAsync(drafted.TaxonomyVersionId, "person-ld", "standards-fixture", cancellationToken).ConfigureAwait(false);
        var profile = (await _jobs.ListAsync(cancellationToken).ConfigureAwait(false))
            .SelectMany(framework => framework.Profiles)
            .Single(item => item.Code == "data-analyst");
        var sql = published.Skills.Single(skill => skill.Code == "sql");
        var modeling = published.Skills.Single(skill => skill.Code == "modeling");
        var beginner = published.Proficiencies.Single(item => item.Code == "beginner");
        var intermediate = published.Proficiencies.Single(item => item.Code == "intermediate");
        var advanced = published.Proficiencies.Single(item => item.Code == "advanced");

        var first = await _benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(
                profile.ProfileId,
                published.TaxonomyVersionId,
                [new TargetDraft(sql.SkillId, intermediate.Rank), new TargetDraft(modeling.SkillId, beginner.Rank)]),
            cancellationToken).ConfigureAwait(false);
        await _benchmarks.PublishAsync(first.BenchmarkVersionId, "person-ld", "standards-fixture", cancellationToken).ConfigureAwait(false);
        await _benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(
                profile.ProfileId,
                published.TaxonomyVersionId,
                [new TargetDraft(sql.SkillId, advanced.Rank), new TargetDraft(modeling.SkillId, beginner.Rank)]),
            cancellationToken).ConfigureAwait(false);
    }
}
