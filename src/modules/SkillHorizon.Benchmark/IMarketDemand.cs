namespace SkillHorizon.Benchmark;

public sealed record MarketDraftView(
    string PracticeCode,
    string Note,
    int PostingCount,
    int RoleCount,
    string Status,
    DateTimeOffset UpdatedAt);

public sealed record MarketPublishedView(
    string PracticeCode,
    string Note,
    int PostingCount,
    int RoleCount,
    DateTimeOffset PublishedAt);

public sealed record MarketPublication(
    string PracticeCode,
    string Note,
    int PostingCount,
    int RoleCount,
    DateTimeOffset PublishedAt);

public interface IMarketDemand
{
    Task<MarketDraftView?> ReadDraftAsync(string practiceCode, CancellationToken cancellationToken);

    Task<MarketPublishedView?> ReadPublishedAsync(string practiceCode, CancellationToken cancellationToken);

    Task<MarketDraftView> SaveDraftAsync(string practiceCode, string note, int postingCount, int roleCount, CancellationToken cancellationToken);

    Task<MarketPublication> PublishAsync(string practiceCode, string subjectId, string correlationId, CancellationToken cancellationToken);
}
