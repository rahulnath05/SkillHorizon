namespace SkillHorizon.Workforce;

public sealed record TeamMember(string EmployeeId, string DisplayName, string Position, bool Attention);

public sealed record TeamSnapshot(DateTimeOffset AsOf, int ReportCount, int AttentionCount, IReadOnlyList<TeamMember> Members);

public sealed record ProfileReview(string EmployeeId, string DisplayName, Guid JobProfileId, string ProfileName);

public sealed record AssessmentReviewItem(string EmployeeId, string DisplayName, Guid ResultId);

public sealed record TeamReviews(IReadOnlyList<ProfileReview> Profiles, IReadOnlyList<AssessmentReviewItem> Assessments);

public sealed record TeamSkillMeasure(
    string SkillCode,
    string SkillName,
    int CurrentRank,
    string CurrentName,
    int TargetRank,
    string TargetName);

public sealed record TeamSkillPerson(string EmployeeId, string DisplayName, string Position, IReadOnlyList<TeamSkillMeasure> Skills);

public sealed record CoachingCommitment(Guid CommitmentId, string EmployeeId, string DisplayName, string Title, DateOnly DueOn);

public interface ITeamWorkspace
{
    Task ProjectAsync(CancellationToken cancellationToken);

    Task<TeamSnapshot?> ReadSnapshotAsync(string managerEmployeeId, CancellationToken cancellationToken);

    Task<TeamReviews> ReadReviewsAsync(string managerEmployeeId, CancellationToken cancellationToken);

    Task ConfirmProfileAsync(string managerEmployeeId, string reportEmployeeId, CancellationToken cancellationToken);

    Task ReviewAssessmentAsync(string managerEmployeeId, string reportEmployeeId, string note, CancellationToken cancellationToken);

    Task<Guid> FileCorrectionAsync(string managerEmployeeId, string reportEmployeeId, string note, CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamSkillPerson>> ReadSkillsAsync(string managerEmployeeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CoachingCommitment>> ListCommitmentsAsync(string managerEmployeeId, CancellationToken cancellationToken);

    Task<CoachingCommitment> CreateCommitmentAsync(string managerEmployeeId, string reportEmployeeId, string title, DateOnly dueOn, CancellationToken cancellationToken);
}
