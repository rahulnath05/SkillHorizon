using Npgsql;

namespace SkillHorizon.Api;

public sealed record BoardSkill(
    Guid SkillId,
    string Code,
    string Name,
    string Category,
    int? Rank,
    bool LevelSet,
    int? SuggestedRank);

public sealed record BoardProfile(
    Guid ProfileId,
    string Code,
    string Name,
    string Purpose,
    string Band,
    string Family,
    string CompetencyCode,
    string FrameworkName,
    string LevelName,
    Guid? BenchmarkVersionId,
    Guid? TaxonomyVersionId,
    int? VersionNumber,
    string? Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SentAt,
    bool Sent,
    IReadOnlyList<BoardSkill> Skills);

public sealed record BoardCompetency(
    string Code,
    string Name,
    string PracticeCode,
    string PracticeName,
    string ComLead,
    string ApprovalStatus,
    IReadOnlyList<BoardProfile> Profiles);

public sealed record BoardTaxonomySkill(
    Guid SkillId,
    string Code,
    string Name,
    string Category,
    string Definition,
    string Status,
    string CompetencyCode,
    string CompetencyName,
    string PracticeCode,
    string PracticeName);

public sealed record BoardProficiency(string Code, string Name, int Rank, string Meaning);

public sealed record BoardTaxonomy(
    Guid TaxonomyVersionId,
    int VersionNumber,
    string Status,
    IReadOnlyList<BoardTaxonomySkill> Skills,
    IReadOnlyList<BoardProficiency> Proficiencies);

public sealed record OverlapNote(string Title, string Detail);

public sealed record ActivationCycle(
    string CycleName,
    string? DueOn,
    bool SelfAssessment,
    bool ManagerValidation,
    bool WorkEvidence);

public sealed record BoardBaseline(string ProfileName, int VersionNumber, DateTimeOffset? PublishedAt, string ComLead);

public sealed record AttentionItem(string Tone, string Title, string Detail, string Action, string Screen);

public sealed record BoardCounts(int Requests, int Approvals, int Activation);

public sealed record SkillAverageRow(
    string SkillCode,
    string SkillName,
    int DeclaredBy,
    decimal? DeclaredAverage,
    decimal? AssessedAverage,
    int AliasCount);

public sealed record CatalogueBoard(
    IReadOnlyList<BoardCompetency> Competencies,
    IReadOnlyList<BoardProfile> Profiles,
    BoardTaxonomy? Taxonomy,
    IReadOnlyList<ProfileRequestRow> Requests,
    IReadOnlyList<OverlapNote> Overlaps,
    ActivationCycle? Cycle,
    IReadOnlyList<BoardBaseline> Baselines,
    IReadOnlyList<PracticeSnapshotRow> Snapshots,
    IReadOnlyList<AttentionItem> Attention,
    BoardCounts Counts);

public sealed class CatalogueBoards
{
    private readonly NpgsqlDataSource _dataSource;

    public CatalogueBoards(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<CatalogueBoard> ReadAsync(string zoneId, CancellationToken cancellationToken)
    {
        var profiles = await Profiles(cancellationToken).ConfigureAwait(false);
        var skills = await Skills(profiles, cancellationToken).ConfigureAwait(false);
        var withSkills = profiles.Select(profile => profile with
        {
            Skills = skills.TryGetValue(profile.BenchmarkVersionId ?? Guid.Empty, out var targets) ? targets : []
        }).ToList();
        var competencies = await Competencies(withSkills, cancellationToken).ConfigureAwait(false);
        var taxonomy = await Taxonomy(cancellationToken).ConfigureAwait(false);
        var requests = await new CatalogueStore(_dataSource).ListRequestsAsync(cancellationToken).ConfigureAwait(false);
        var overlaps = await Overlaps(cancellationToken).ConfigureAwait(false);
        var cycle = await Cycle(zoneId, cancellationToken).ConfigureAwait(false);
        var baselines = await Baselines(cancellationToken).ConfigureAwait(false);
        var snapshots = await Snapshots(cancellationToken).ConfigureAwait(false);
        var attention = BuildAttention(competencies, requests);
        var counts = new BoardCounts(
            requests.Count(request => request.RequestStatus is "new" or "needs-context"),
            competencies.Count(competency => competency.ApprovalStatus == "changes-requested" || ReadyToSend(competency)),
            competencies.Count(competency => competency.ApprovalStatus == "approved"));
        return new CatalogueBoard(competencies, withSkills, taxonomy, requests, overlaps, cycle, baselines, snapshots, attention, counts);
    }

    public async Task<IReadOnlyList<SkillAverageRow>> SkillAveragesAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT codes.skill_code,
                   COALESCE(names.skill_name, codes.skill_code),
                   COALESCE(declared.declared_by, 0),
                   declared.declared_avg,
                   assessed.assessed_avg,
                   COALESCE(aliases.alias_count, 0)
            FROM (
                SELECT skill_code FROM readiness.declaration
                UNION
                SELECT skill_code FROM readiness.proficiency
                UNION
                SELECT skill_code FROM readiness.skill_alias
            ) AS codes
            LEFT JOIN (
                SELECT DISTINCT ON (skill_code) skill_code, skill_name
                FROM taxonomy.version_skill
                ORDER BY skill_code, skill_name
            ) AS names ON names.skill_code = codes.skill_code
            LEFT JOIN (
                SELECT skill_code, count(*)::int AS declared_by, round(avg(declared_rank), 2) AS declared_avg
                FROM readiness.declaration
                GROUP BY skill_code
            ) AS declared ON declared.skill_code = codes.skill_code
            LEFT JOIN (
                SELECT skill_code, round(avg(proficiency_rank), 2) AS assessed_avg
                FROM readiness.proficiency
                GROUP BY skill_code
            ) AS assessed ON assessed.skill_code = codes.skill_code
            LEFT JOIN (
                SELECT skill_code, count(*)::int AS alias_count
                FROM readiness.skill_alias
                GROUP BY skill_code
            ) AS aliases ON aliases.skill_code = codes.skill_code
            ORDER BY codes.skill_code
            """);
        var rows = new List<SkillAverageRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new SkillAverageRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.GetInt32(5)));
        }

        return rows;
    }

    private async Task<List<BoardProfile>> Profiles(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT DISTINCT ON (profile.profile_id)
                   profile.profile_id,
                   profile.code,
                   profile.display_name,
                   profile.purpose,
                   profile.band,
                   profile.family_name,
                   COALESCE(profile.competency_code, ''),
                   framework.display_name,
                   level.display_name,
                   version.benchmark_version_id,
                   version.taxonomy_version_id,
                   version.version_number,
                   version.status,
                   version.published_at,
                   submission.sent_at
            FROM job_architecture.job_profile AS profile
            JOIN job_architecture.framework AS framework ON framework.framework_id = profile.framework_id
            JOIN job_architecture.level AS level ON level.level_id = profile.level_id
            LEFT JOIN benchmark.version AS version ON version.job_profile_id = profile.profile_id
            LEFT JOIN catalogue.submission AS submission ON submission.benchmark_version_id = version.benchmark_version_id
            ORDER BY profile.profile_id,
                     CASE version.status WHEN 'draft' THEN 0 WHEN 'published' THEN 1 ELSE 2 END,
                     version.version_number DESC NULLS LAST
            """);
        var rows = new List<BoardProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sentAt = reader.IsDBNull(14) ? (DateTimeOffset?)null : Timestamp(reader.GetValue(14));
            rows.Add(new BoardProfile(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : Timestamp(reader.GetValue(13)),
                sentAt,
                sentAt is not null,
                []));
        }

        return rows;
    }

    private async Task<Dictionary<Guid, List<BoardSkill>>> Skills(IReadOnlyList<BoardProfile> profiles, CancellationToken cancellationToken)
    {
        var ids = profiles.Where(profile => profile.BenchmarkVersionId is not null).Select(profile => profile.BenchmarkVersionId!.Value).ToArray();
        var map = new Dictionary<Guid, List<BoardSkill>>();
        if (ids.Length == 0)
            return map;

        await using var command = _dataSource.CreateCommand(
            """
            SELECT target.benchmark_version_id, target.skill_id, target.skill_code, target.skill_name,
                   COALESCE(skill.category, ''), target.proficiency_rank, target.level_set, target.suggested_rank
            FROM benchmark.target AS target
            LEFT JOIN taxonomy.skill AS skill ON skill.code = target.skill_code
            WHERE target.benchmark_version_id = ANY($1)
            ORDER BY target.skill_name
            """);
        command.Parameters.AddWithValue(ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var versionId = reader.GetGuid(0);
            if (!map.TryGetValue(versionId, out var list))
            {
                list = [];
                map[versionId] = list;
            }

            var levelSet = reader.GetBoolean(6);
            list.Add(new BoardSkill(
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                levelSet ? reader.GetInt32(5) : null,
                levelSet,
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }

        return map;
    }

    private async Task<List<BoardCompetency>> Competencies(IReadOnlyList<BoardProfile> profiles, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT competency.code, competency.display_name, practice.code, practice.display_name,
                   competency.com_lead, competency.approval_status
            FROM taxonomy.competency AS competency
            JOIN taxonomy.practice AS practice ON practice.practice_id = competency.practice_id
            ORDER BY practice.display_name, competency.display_name
            """);
        var rows = new List<BoardCompetency>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var code = reader.GetString(0);
            rows.Add(new BoardCompetency(
                code,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                profiles.Where(profile => profile.CompetencyCode == code).OrderBy(profile => profile.Name).ToList()));
        }

        return rows;
    }

    private async Task<BoardTaxonomy?> Taxonomy(CancellationToken cancellationToken)
    {
        Guid? versionId = null;
        var number = 0;
        var status = "";
        await using (var command = _dataSource.CreateCommand(
            """
            SELECT taxonomy_version_id, version_number, status
            FROM taxonomy.version
            WHERE status = 'draft'
               OR (status = 'published' AND NOT EXISTS (SELECT 1 FROM taxonomy.version WHERE status = 'draft'))
            ORDER BY CASE status WHEN 'draft' THEN 0 ELSE 1 END
            LIMIT 1
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            versionId = reader.GetGuid(0);
            number = reader.GetInt32(1);
            status = reader.GetString(2);
        }

        var skills = new List<BoardTaxonomySkill>();
        await using (var command = _dataSource.CreateCommand(
            """
            SELECT skill_id, skill_code, skill_name, category, definition, status,
                   competency_code, competency_name, practice_code, practice_name
            FROM taxonomy.version_skill
            WHERE taxonomy_version_id = $1
            ORDER BY category, skill_name
            """))
        {
            command.Parameters.AddWithValue(versionId!.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                skills.Add(new BoardTaxonomySkill(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9)));
            }
        }

        var proficiencies = new List<BoardProficiency>();
        await using (var command = _dataSource.CreateCommand(
            """
            SELECT code, display_name, rank, meaning
            FROM taxonomy.proficiency
            WHERE taxonomy_version_id = $1
            ORDER BY rank
            """))
        {
            command.Parameters.AddWithValue(versionId!.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                proficiencies.Add(new BoardProficiency(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        }

        return new BoardTaxonomy(versionId!.Value, number, status, skills, proficiencies);
    }

    private async Task<List<OverlapNote>> Overlaps(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("SELECT title, detail FROM taxonomy.overlap_note ORDER BY title");
        var rows = new List<OverlapNote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(new OverlapNote(reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    private async Task<ActivationCycle?> Cycle(string zoneId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT cycle_name, due_on, self_assessment, manager_validation, work_evidence
            FROM activation.cycle
            WHERE zone_id = $1
            """);
        command.Parameters.AddWithValue(zoneId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        var due = reader.IsDBNull(1) ? null : reader.GetDateTime(1).ToString("yyyy-MM-dd");
        return new ActivationCycle(reader.GetString(0), due, reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4));
    }

    private async Task<List<BoardBaseline>> Baselines(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT profile.display_name, version.version_number, version.published_at, COALESCE(competency.com_lead, '')
            FROM benchmark.version AS version
            JOIN job_architecture.job_profile AS profile ON profile.profile_id = version.job_profile_id
            LEFT JOIN taxonomy.competency AS competency ON competency.code = profile.competency_code
            WHERE version.status = 'published'
            ORDER BY version.published_at DESC NULLS LAST, profile.display_name
            """);
        var rows = new List<BoardBaseline>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new BoardBaseline(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : Timestamp(reader.GetValue(2)),
                reader.GetString(3)));
        }

        return rows;
    }

    private async Task<List<PracticeSnapshotRow>> Snapshots(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT subject_key, as_of, member_count, measured_count, gap_count, cohort_count
            FROM readiness.snapshot
            WHERE subject_kind = 'practice'
            ORDER BY subject_key
            """);
        var rows = new List<PracticeSnapshotRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PracticeSnapshotRow(
                reader.GetString(0),
                Timestamp(reader.GetValue(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return rows;
    }

    private static List<AttentionItem> BuildAttention(IReadOnlyList<BoardCompetency> competencies, IReadOnlyList<ProfileRequestRow> requests)
    {
        var items = new List<AttentionItem>();
        foreach (var competency in competencies.Where(item => item.ApprovalStatus == "approved"))
            items.Add(new AttentionItem("approved", $"Activate {competency.Name}", "This competency is approved and can be activated.", "Activate", "activation"));
        foreach (var profile in competencies.SelectMany(competency => competency.Profiles))
        {
            var missing = profile.Skills.Count(skill => !skill.LevelSet);
            if (missing > 0)
                items.Add(new AttentionItem("missing", $"Finish {profile.Name}", $"{missing} benchmark levels are not set.", "Continue", "workspace"));
        }

        foreach (var request in requests.Where(request => request.ProfileId is null && request.RequestStatus is "new" or "needs-context"))
        {
            var title = request.Title.Length > 0 ? request.Title : request.DisplayName;
            items.Add(new AttentionItem("request", title, request.Note, "Open", "requests"));
        }

        foreach (var competency in competencies.Where(item => item.ApprovalStatus == "changes-requested"))
            items.Add(new AttentionItem("returned", $"Resolve {competency.Name} changes", "The competency lead asked for revisions.", "Resolve", "workspace"));
        return items.Take(4).ToList();
    }

    private static bool ReadyToSend(BoardCompetency competency)
    {
        if (competency.ApprovalStatus != "draft" || competency.Profiles.Count == 0)
            return false;
        var complete = competency.Profiles.All(profile => profile.Skills.Count > 0 && profile.Skills.All(skill => skill.LevelSet));
        return complete && competency.Profiles.Any(profile => profile.Status == "draft" && !profile.Sent);
    }

    private static DateTimeOffset Timestamp(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
        _ => throw new InvalidOperationException("A catalogue row has no timestamp.")
    };
}
