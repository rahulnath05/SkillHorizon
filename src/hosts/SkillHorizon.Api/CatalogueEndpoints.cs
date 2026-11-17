using SkillHorizon.Access;
using SkillHorizon.Benchmark;
using SkillHorizon.Hosting;
using SkillHorizon.JobArchitecture;
using SkillHorizon.Taxonomy;

namespace SkillHorizon.Api;

public static class CatalogueEndpoints
{
    public static WebApplication MapCatalogue(this WebApplication app)
    {
        app.MapGet("/catalogue/taxonomy", Taxonomy);
        app.MapGet("/catalogue/benchmark", Benchmark);
        app.MapGet("/catalogue/requests", Requests);
        app.MapPost("/catalogue/requests/{requestId:guid}/open", Open);
        app.MapGet("/catalogue/approvals", Approvals);
        app.MapPost("/catalogue/submissions", Send);
        app.MapGet("/catalogue/review", Review);
        app.MapGet("/catalogue/overview", Overview);
        app.MapGet("/catalogue/boards", Boards);
        app.MapPost("/catalogue/profiles", CreateProfile);
        app.MapPost("/catalogue/taxonomy/activate", ActivateSkill);
        app.MapGet("/studio/skill-averages", SkillAverages);
        app.MapGet("/catalogue/market", Market);
        app.MapPost("/commands/market/draft", SaveMarket);
        app.MapPost("/commands/market/publish", PublishMarket);
        return app;
    }

    private static async Task<IResult> Taxonomy(
        HttpContext context,
        IGrantCheck grants,
        ITaxonomyAuthoring taxonomy,
        IReferenceLibrary reference,
        CancellationToken cancellationToken)
    {
        var workspace = await Held(context, grants, cancellationToken).ConfigureAwait(false);
        if (workspace is null)
            return Forbidden("A held workspace is required to read the taxonomy.");

        var authoring = workspace == Workspaces.LearningAndDevelopment;
        var version = authoring
            ? await taxonomy.FindByStatusAsync("draft", cancellationToken).ConfigureAwait(false)
                ?? await taxonomy.FindByStatusAsync("published", cancellationToken).ConfigureAwait(false)
            : await reference.ReadPublishedTaxonomyAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { source = authoring ? "authoring" : "reference", version = Browser(version) });
    }

    private static async Task<IResult> Benchmark(
        HttpContext context,
        IGrantCheck grants,
        IJobArchitecture jobs,
        ITaxonomyAuthoring taxonomy,
        IBenchmarkAuthoring benchmarks,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;

        var profile = await jobs.FindProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
            return Results.Problem(title: "Profile missing", detail: "That job profile does not exist.", statusCode: StatusCodes.Status400BadRequest);

        var drafts = await benchmarks.ListDraftsAsync(cancellationToken).ConfigureAwait(false);
        var draft = drafts.FirstOrDefault(item => item.JobProfileId == profileId);
        var history = await benchmarks.HistoryAsync(profileId, cancellationToken).ConfigureAwait(false);
        var published = history.FirstOrDefault(item => item.Status == "published");
        var pinnedId = draft?.TaxonomyVersionId ?? published?.TaxonomyVersionId;
        var pinned = pinnedId is Guid versionId
            ? await taxonomy.FindAsync(versionId, cancellationToken).ConfigureAwait(false)
            : await taxonomy.FindByStatusAsync("published", cancellationToken).ConfigureAwait(false);
        if (pinned is null || pinned.Status == "draft" || pinned.Proficiencies.Count == 0)
            return Results.Problem(title: "Taxonomy missing", detail: "A benchmark is pinned to a published taxonomy version.", statusCode: StatusCodes.Status400BadRequest);

        var ranks = (draft ?? published)?.Targets.ToDictionary(target => target.SkillId, target => target.ProficiencyRank)
            ?? new Dictionary<Guid, int>();
        var floor = pinned.Proficiencies.Min(item => item.Rank);
        return Results.Ok(new
        {
            profile = new
            {
                profile.ProfileId,
                profile.ProfileName,
                profile.FrameworkName,
                profile.LevelName
            },
            taxonomyVersionId = pinned.TaxonomyVersionId,
            benchmarkVersionId = draft?.BenchmarkVersionId,
            versionNumber = draft?.VersionNumber,
            proficiencies = pinned.Proficiencies.Select(item => new { item.Code, item.Name, item.Rank }),
            skills = pinned.Skills
                .OrderBy(skill => skill.PracticeName)
                .ThenBy(skill => skill.CompetencyName)
                .ThenBy(skill => skill.Name)
                .Select(skill => new
                {
                    skill.SkillId,
                    skillName = skill.Name,
                    skill.CompetencyName,
                    skill.PracticeName,
                    proficiencyRank = ranks.TryGetValue(skill.SkillId, out var rank) ? rank : floor
                })
        });
    }

    private static async Task<IResult> Requests(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var requests = await new CatalogueStore(dataSource).ListRequestsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { requests });
    }

    private static async Task<IResult> Open(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, Guid requestId, OpenBody? body, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var profile = await new CatalogueStore(dataSource).OpenAsync(
                requestId,
                body?.FrameworkCode ?? "",
                body?.LevelCode ?? "",
                body?.ProfileName ?? "",
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { profile, published = false });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Request refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Approvals(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(new { drafts = await new CatalogueStore(dataSource).ListSentAsync(cancellationToken).ConfigureAwait(false) });
    }

    private static async Task<IResult> Send(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, SendBody? body, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var draft = await new CatalogueStore(dataSource).SendAsync(body?.BenchmarkVersionId ?? Guid.Empty, Subject(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(draft);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Submission refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> Review(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var refused = await Require(context, grants, Workspaces.CompetencyLead, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        return Results.Ok(new { drafts = await new CatalogueStore(dataSource).ListSentAsync(cancellationToken).ConfigureAwait(false) });
    }

    private static async Task<IResult> Overview(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var overview = await new CatalogueStore(dataSource).ReadOverviewAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new
        {
            snapshots = overview.Snapshots.Select(snapshot => new
            {
                snapshot.PracticeCode,
                snapshot.AsOf,
                snapshot.MemberCount,
                snapshot.MeasuredCount,
                snapshot.GapCount,
                snapshot.CohortCount
            }),
            overview.RequestCount,
            overview.ApprovalCount
        });
    }

    private static async Task<IResult> Boards(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, Microsoft.Extensions.Options.IOptions<SkillHorizon.Hosting.SkillHorizonOptions> options, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var board = await new CatalogueBoards(dataSource).ReadAsync(options.Value.ZoneId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(board);
    }

    private static async Task<IResult> CreateProfile(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, CreateProfileBody? body, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var profile = await new CatalogueStore(dataSource).CreateProfileAsync(
                body?.FrameworkCode ?? "",
                body?.LevelCode ?? "",
                body?.ProfileName ?? "",
                body?.Purpose ?? "",
                body?.Band ?? "",
                body?.Family ?? "",
                body?.CompetencyCode ?? "",
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { profile, published = false });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Profile refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ActivateSkill(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, ActivateSkillBody? body, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            await new CatalogueStore(dataSource).ActivateSkillAsync(body?.Code ?? "", cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Taxonomy refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> SkillAverages(HttpContext context, IGrantCheck grants, Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var rows = await new CatalogueBoards(dataSource).SkillAveragesAsync(cancellationToken).ConfigureAwait(false);
        SkillAverageRow? widest = null;
        decimal widestGap = 0;
        foreach (var row in rows)
        {
            if (row.DeclaredAverage is not decimal declared || row.AssessedAverage is not decimal assessed)
                continue;
            var gap = declared - assessed;
            if (widest is null || gap > widestGap)
            {
                widest = row;
                widestGap = gap;
            }
        }

        string? note = widest is null
            ? null
            : $"{widest.SkillName} is {widestGap:0.00} levels from the declared average on the stored rows.";
        return Results.Ok(new { averages = rows, note });
    }

    private static async Task<IResult> Market(HttpContext context, IGrantCheck grants, IMarketDemand market, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        var practice = context.Request.Query["practice"].ToString();
        return Results.Ok(new
        {
            draft = await market.ReadDraftAsync(practice, cancellationToken).ConfigureAwait(false),
            published = await market.ReadPublishedAsync(practice, cancellationToken).ConfigureAwait(false)
        });
    }

    private static async Task<IResult> SaveMarket(HttpContext context, IGrantCheck grants, IMarketDemand market, MarketBody? body, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var draft = await market.SaveDraftAsync(body?.PracticeCode ?? "", body?.Note ?? "", body?.PostingCount ?? -1, body?.RoleCount ?? -1, cancellationToken).ConfigureAwait(false);
            return Results.Ok(draft);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Market demand refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> PublishMarket(HttpContext context, IGrantCheck grants, IMarketDemand market, MarketBody? body, CancellationToken cancellationToken)
    {
        var refused = await Learning(context, grants, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;
        try
        {
            var publication = await market.PublishAsync(body?.PracticeCode ?? "", Subject(context), Correlation(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(publication);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(title: "Market demand refused", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static string Correlation(HttpContext context) =>
        context.Items[SkillHorizonContext.CorrelationItem]?.ToString() is { Length: > 0 } value ? value : "market";

    private static object? Browser(TaxonomyVersionView? version)
    {
        if (version is null)
            return null;
        var competencies = version.Skills
            .GroupBy(skill => (skill.PracticeCode, skill.PracticeName, skill.CompetencyCode, skill.CompetencyName))
            .OrderBy(group => group.Key.PracticeName)
            .ThenBy(group => group.Key.CompetencyName)
            .Select(group => new
            {
                code = group.Key.CompetencyCode,
                name = group.Key.CompetencyName,
                practiceCode = group.Key.PracticeCode,
                practiceName = group.Key.PracticeName,
                skills = group.OrderBy(skill => skill.Name).Select(skill => new { skill.Code, skill.Name }).ToArray()
            });
        return new
        {
            version.TaxonomyVersionId,
            version.VersionNumber,
            version.Status,
            competencies,
            proficiencies = version.Proficiencies.OrderBy(item => item.Rank).Select(item => new { item.Code, item.Name, item.Rank })
        };
    }

    private static Task<IResult?> Learning(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken) =>
        Require(context, grants, Workspaces.LearningAndDevelopment, cancellationToken);

    private static async Task<string?> Held(HttpContext context, IGrantCheck grants, CancellationToken cancellationToken)
    {
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (active is not (Workspaces.Employee or Workspaces.PeopleManager or Workspaces.LearningAndDevelopment or Workspaces.CompetencyLead))
            return null;
        return await grants.HasGrantAsync(Subject(context), active, cancellationToken).ConfigureAwait(false) ? active : null;
    }

    private static async Task<IResult?> Require(HttpContext context, IGrantCheck grants, string workspace, CancellationToken cancellationToken)
    {
        var active = context.Request.Headers[SkillHorizonContext.WorkspaceHeader].ToString();
        if (string.Equals(active, workspace, StringComparison.Ordinal)
            && await grants.HasGrantAsync(Subject(context), workspace, cancellationToken).ConfigureAwait(false))
            return null;
        return Forbidden("This page is refused for the active workspace.");
    }

    private static IResult Forbidden(string detail) =>
        Results.Problem(title: "Grant required", detail: detail, statusCode: StatusCodes.Status403Forbidden);

    private static string Subject(HttpContext context) => context.User.FindFirst("sub")?.Value ?? "";

    private sealed record OpenBody(string? ProfileName, string? FrameworkCode, string? LevelCode);

    private sealed record SendBody(Guid BenchmarkVersionId);

    private sealed record MarketBody(string? PracticeCode, string? Note, int? PostingCount, int? RoleCount);

    private sealed record CreateProfileBody(string? ProfileName, string? FrameworkCode, string? LevelCode, string? Purpose, string? Band, string? Family, string? CompetencyCode);

    private sealed record ActivateSkillBody(string? Code);
}
