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
        var existing = await _jobs.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(framework => framework.Code == FrameworkCode))
        {
            await _scope.GrantAsync("person-lead", "data", cancellationToken).ConfigureAwait(false);
            return;
        }

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
        await _scope.GrantAsync("person-lead", "data", cancellationToken).ConfigureAwait(false);
        await _benchmarks.SaveDraftAsync(
            new BenchmarkDraftWrite(
                profile.ProfileId,
                published.TaxonomyVersionId,
                [new TargetDraft(sql.SkillId, advanced.Rank), new TargetDraft(modeling.SkillId, beginner.Rank)]),
            cancellationToken).ConfigureAwait(false);
    }

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
