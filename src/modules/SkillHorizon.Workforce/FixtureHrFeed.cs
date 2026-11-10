namespace SkillHorizon.Workforce;

public sealed class FixtureHrFeed : IHrFeed
{
    public const string Multi = "person-multi";
    public const string Single = "person-single";
    public const string NoGrant = "person-nogrant";
    public const string Learning = "person-ld";
    public const string Asha = "asha-rao";
    public const string Dev = "dev-kapoor";
    public const string Nisha = "nisha-iyer";
    public const string OtherManager = "other-manager";
    public const string OtherReport = "other-report";

    private static readonly IReadOnlyList<HrRecord> People =
    [
        new(Single, "LE-LOCAL", "IN", null, "active", true, Single),
        new(Multi, "LE-LOCAL", "IN", null, "active", true, Multi),
        new(Asha, "LE-LOCAL", "IN", Multi, "active", true, "Asha Rao"),
        new(Dev, "LE-LOCAL", "IN", Multi, "active", false, "Dev Kapoor"),
        new(Nisha, "LE-LOCAL", "IN", Multi, "leaver", false, "Nisha Iyer"),
        new(NoGrant, "LE-LOCAL", "IN", null, "active", false, NoGrant),
        new(Learning, "LE-LOCAL", "IN", null, "active", false, Learning),
        new(OtherManager, "LE-LOCAL", "IN", null, "active", true, "Other Manager"),
        new(OtherReport, "LE-LOCAL", "SG", OtherManager, "active", true, "Other Report")
    ];

    public Task<IReadOnlyList<HrRecord>> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(People);
}
