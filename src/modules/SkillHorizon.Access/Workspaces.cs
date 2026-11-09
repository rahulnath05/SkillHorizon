namespace SkillHorizon.Access;

public static class Workspaces
{
    public const string Employee = "employee";
    public const string PeopleManager = "people_manager";
    public const string CompetencyLead = "competency_lead";
    public const string LearningAndDevelopment = "learning_and_development";
    public const string PlatformAdmin = "platform_admin";

    public static readonly IReadOnlyList<string> All =
    [
        Employee,
        PeopleManager,
        CompetencyLead,
        LearningAndDevelopment,
        PlatformAdmin
    ];

    public static bool IsKnown(string? workspace) =>
        workspace is not null && All.Contains(workspace, StringComparer.Ordinal);
}
