using Npgsql;
using SkillHorizon.Attestation;
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
    private readonly IPracticeScope _scope;
    private readonly NpgsqlDataSource _dataSource;

    public StandardsFixture(
        IJobArchitecture jobs,
        ITaxonomyAuthoring taxonomy,
        IBenchmarkAuthoring benchmarks,
        IPracticeScope scope,
        NpgsqlDataSource dataSource)
    {
        _jobs = jobs;
        _taxonomy = taxonomy;
        _benchmarks = benchmarks;
        _scope = scope;
        _dataSource = dataSource;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureLead(cancellationToken).ConfigureAwait(false);
        await _jobs.ImportAsync([Framework()], cancellationToken).ConfigureAwait(false);
        if (await _taxonomy.FindByStatusAsync("published", cancellationToken).ConfigureAwait(false) is null)
            await PublishInitialStandards(cancellationToken).ConfigureAwait(false);
        await EnsureConsultantBenchmark(cancellationToken).ConfigureAwait(false);
        await _scope.GrantAsync("person-lead", "data", cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishInitialStandards(CancellationToken cancellationToken)
    {
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

    private async Task EnsureConsultantBenchmark(CancellationToken cancellationToken)
    {
        var published = await _taxonomy.FindByStatusAsync("published", cancellationToken).ConfigureAwait(false);
        if (published is null)
            return;

        var profile = (await _jobs.ListAsync(cancellationToken).ConfigureAwait(false))
            .SelectMany(framework => framework.Profiles)
            .SingleOrDefault(item => item.Code == "data-consultant");
        if (profile is null)
            return;

        var versions = await _benchmarks.PublishedAsync(cancellationToken).ConfigureAwait(false);
        if (versions.Any(version => version.ProfileCode == "data-consultant" && version.Status == "published"))
            return;

        var drafts = await _benchmarks.ListDraftsAsync(cancellationToken).ConfigureAwait(false);
        var draft = drafts.FirstOrDefault(version => version.ProfileCode == "data-consultant");
        if (draft is null)
        {
            var sql = published.Skills.Single(skill => skill.Code == "sql");
            var modeling = published.Skills.Single(skill => skill.Code == "modeling");
            var intermediate = published.Proficiencies.Single(item => item.Code == "intermediate");
            var advanced = published.Proficiencies.Single(item => item.Code == "advanced");
            draft = await _benchmarks.SaveDraftAsync(
                new BenchmarkDraftWrite(
                    profile.ProfileId,
                    published.TaxonomyVersionId,
                    [new TargetDraft(sql.SkillId, advanced.Rank), new TargetDraft(modeling.SkillId, intermediate.Rank)]),
                cancellationToken).ConfigureAwait(false);
        }

        await _benchmarks.PublishAsync(draft.BenchmarkVersionId, "person-ld", "standards-fixture", cancellationToken).ConfigureAwait(false);
    }

    private static FrameworkImport Framework() =>
        new(
            FrameworkCode,
            "Consulting",
            [
                new LevelImport("associate", "Associate", 1),
                new LevelImport("consultant", "Consultant", 2)
            ],
            [
                new ProfileImport("data-analyst", "Data analyst", "associate"),
                new ProfileImport("data-consultant", "Data consultant", "consultant")
            ]);

    private async Task EnsureLead(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
            VALUES ('55555555-5555-5555-5555-555555555555', 'person-lead', 'competency_lead', now())
            ON CONFLICT (subject_id, workspace) DO NOTHING
            """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
