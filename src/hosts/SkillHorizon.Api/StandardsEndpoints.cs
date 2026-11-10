using SkillHorizon.Access;
using SkillHorizon.Benchmark;
using SkillHorizon.Hosting;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Taxonomy;

namespace SkillHorizon.Api;

public static class StandardsEndpoints
{
    public static WebApplication MapStandards(this WebApplication app)
    {
        app.MapGet("/standards", Read);
        app.MapPost("/commands/job-architecture/import", ImportJobs);
        app.MapPost("/commands/taxonomy/draft", SaveTaxonomy);
        app.MapPost("/commands/taxonomy/publish", PublishTaxonomy);
        app.MapPost("/commands/benchmarks/draft", SaveBenchmark);
        app.MapPost("/commands/benchmarks/publish", PublishBenchmark);
        app.MapGet("/benchmarks/drafts", Drafts);
        app.MapGet("/benchmarks/history", AuthoringHistory);
        app.MapGet("/reference/benchmarks/history", ReferenceHistory);
        return app;
    }

    private static async Task<IResult> Read(
        HttpContext context,
        IGrantCheck grants,
        IJobArchitecture jobs,
        IBenchmarkAuthoring benchmarks,
        IReferenceLibrary reference,
        ITaxonomyAuthoring taxonomy,
        CancellationToken cancellationToken)
    {
        var workspace = await HeldWorkspace(context, grants, cancellationToken).ConfigureAwait(false);
        if (workspace is null)
            return Forbidden("A held workspace is required to read standards.");

        var frameworks = await jobs.ListAsync(cancellationToken).ConfigureAwait(false);
        if (workspace == Workspaces.LearningAndDevelopment)
        {
            var drafts = await benchmarks.ListDraftsAsync(cancellationToken).ConfigureAwait(false);
            var authoringDrafts = new List<object>();
            foreach (var draft in drafts)
            {
                var pinned = await taxonomy.FindAsync(draft.TaxonomyVersionId, cancellationToken).ConfigureAwait(false);
                authoringDrafts.Add(new { draft, proficiencies = pinned?.Proficiencies ?? [] });
            }

            var history = await benchmarks.PublishedAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { source = "authoring", frameworks, drafts = authoringDrafts, history });
        }

        var published = await reference.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { source = "reference", frameworks, drafts = Array.Empty<object>(), history = published });
    }

    private static Task<IResult> ImportJobs(HttpContext context, IGrantCheck grants, IJobArchitecture jobs, FrameworkImport[]? body, CancellationToken cancellationToken) =>
        Command(context, grants, cancellationToken, async () =>
        {
            await jobs.ImportAsync(body ?? [], cancellationToken).ConfigureAwait(false);
            return Results.Ok(await jobs.ListAsync(cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> SaveTaxonomy(HttpContext context, IGrantCheck grants, ITaxonomyAuthoring taxonomy, TaxonomyDraft? body, CancellationToken cancellationToken) =>
        Command(context, grants, cancellationToken, async () =>
        {
            if (body is null)
                throw new InvalidOperationException("A taxonomy draft needs a practice and a proficiency scale.");
            return Results.Ok(await taxonomy.SaveDraftAsync(body, cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> PublishTaxonomy(HttpContext context, IGrantCheck grants, ITaxonomyAuthoring taxonomy, PublishTaxonomyRequest? body, CancellationToken cancellationToken) =>
        Command(context, grants, cancellationToken, async () =>
            Results.Ok(await taxonomy.PublishAsync(body?.TaxonomyVersionId ?? Guid.Empty, Subject(context), Correlation(context), cancellationToken).ConfigureAwait(false)));

    private static Task<IResult> SaveBenchmark(HttpContext context, IGrantCheck grants, IBenchmarkAuthoring benchmarks, BenchmarkDraftWrite? body, CancellationToken cancellationToken) =>
        Command(context, grants, cancellationToken, async () =>
        {
            if (body is null)
                throw new InvalidOperationException("A benchmark draft needs a job profile and skill targets.");
            return Results.Ok(await benchmarks.SaveDraftAsync(body, cancellationToken).ConfigureAwait(false));
        });

    private static Task<IResult> PublishBenchmark(HttpContext context, IGrantCheck grants, IBenchmarkAuthoring benchmarks, PublishBenchmarkRequest? body, CancellationToken cancellationToken) =>
        Command(context, grants, cancellationToken, async () =>
            Results.Ok(await benchmarks.PublishAsync(body?.BenchmarkVersionId ?? Guid.Empty, Subject(context), Correlation(context), cancellationToken).ConfigureAwait(false)));

    private static async Task<IResult> Drafts(HttpContext context, IGrantCheck grants, IBenchmarkAuthoring benchmarks, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.LearningAndDevelopment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(new { drafts = await benchmarks.ListDraftsAsync(cancellationToken).ConfigureAwait(false) });
    }

    private static async Task<IResult> AuthoringHistory(HttpContext context, IGrantCheck grants, IBenchmarkAuthoring benchmarks, Guid profileId, CancellationToken cancellationToken)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.LearningAndDevelopment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(new { history = await benchmarks.HistoryAsync(profileId, cancellationToken).ConfigureAwait(false) });
    }

    private static async Task<IResult> ReferenceHistory(HttpContext context, IGrantCheck grants, IReferenceLibrary reference, Guid profileId, CancellationToken cancellationToken)
    {
        var workspace = await HeldWorkspace(context, grants, cancellationToken).ConfigureAwait(false);
        if (workspace is null)
            return Forbidden("A held workspace is required to read a published benchmark.");
        return Results.Ok(new { history = await reference.HistoryAsync(profileId, cancellationToken).ConfigureAwait(false) });
    }

    private static async Task<IResult> Command(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken, Func<Task<IResult>> action)
    {
        var refused = await RefuseUnless(context, grants, Workspaces.LearningAndDevelopment, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Not published", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult?> RefuseUnless(HttpContext context, IGrantCheck grants, string workspace, CancellationToken cancellationToken)
    {
        var subject = Subject(context);
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (string.Equals(active, workspace, StringComparison.Ordinal)
            && await grants.HasGrantAsync(subject, workspace, cancellationToken).ConfigureAwait(false))
            return null;
        return Forbidden("This command is refused until the L&D grant is the active workspace.");
    }

    private static async Task<string?> HeldWorkspace(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken)
    {
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (active is not (Workspaces.Employee or Workspaces.PeopleManager or Workspaces.LearningAndDevelopment))
            return null;
        var subject = Subject(context);
        return await grants.HasGrantAsync(subject, active, cancellationToken).ConfigureAwait(false) ? active : null;
    }

    private static IResult Forbidden(string detail) =>
        Results.Problem(title: "Grant required", detail: detail, statusCode: StatusCodes.Status403Forbidden);

    private static string Subject(HttpContext context) =>
        context.User.FindFirst("sub")?.Value ?? "";

    private static string Correlation(HttpContext context) =>
        context.Items[SkillHorizonContext.CorrelationItem]?.ToString() is { Length: > 0 } value ? value : "standards";

    private sealed record PublishTaxonomyRequest(Guid TaxonomyVersionId);

    private sealed record PublishBenchmarkRequest(Guid BenchmarkVersionId);
}
