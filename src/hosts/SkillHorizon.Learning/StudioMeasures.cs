using Npgsql;

namespace SkillHorizon.Learning;

public sealed partial class AssessmentBank
{
    public async Task<object> MeasuresAsync(string bearer, string workspace, string correlationId, CancellationToken cancellationToken)
    {
        await RequireAuthor(bearer, workspace, correlationId, cancellationToken).ConfigureAwait(false);
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var exposure = await Read(connection, "SELECT skill_code, item_label, exposed_count, ceiling_percent FROM assessment.engine_exposure ORDER BY skill_code, item_label", cancellationToken, reader => new
        {
            skillCode = reader.GetString(0),
            itemLabel = reader.GetString(1),
            exposedCount = reader.GetInt32(2),
            ceilingPercent = reader.GetDecimal(3)
        }).ConfigureAwait(false);
        var selection = await Read(connection, "SELECT sitting_label, step_number, skill_code, rule_name, estimate, band, outcome FROM assessment.engine_selection ORDER BY sitting_label, step_number", cancellationToken, reader => new
        {
            sittingLabel = reader.GetString(0),
            stepNumber = reader.GetInt32(1),
            skillCode = reader.GetString(2),
            ruleName = reader.GetString(3),
            estimate = reader.GetDecimal(4),
            band = reader.GetDecimal(5),
            outcome = reader.GetString(6)
        }).ConfigureAwait(false);
        var stopping = await Read(connection, "SELECT rule_name, detail, stop_band, item_cap FROM assessment.engine_stopping ORDER BY rule_name", cancellationToken, reader => new
        {
            ruleName = reader.GetString(0),
            detail = reader.GetString(1),
            stopBand = reader.GetDecimal(2),
            itemCap = reader.GetInt32(3)
        }).ConfigureAwait(false);
        var itemStatistics = await Read(connection, "SELECT skill_code, item_count, difficulty, discrimination, median_seconds, disposition FROM assessment.item_statistic ORDER BY skill_code", cancellationToken, reader => new
        {
            skillCode = reader.GetString(0),
            itemCount = reader.GetInt32(1),
            difficulty = reader.GetDecimal(2),
            discrimination = reader.GetDecimal(3),
            medianSeconds = reader.GetInt32(4),
            disposition = reader.GetString(5)
        }).ConfigureAwait(false);
        var geography = await Read(connection, "SELECT place_name, invited_count, sat_count, median_seconds, not_started_count, target_percent FROM assessment.geography_completion ORDER BY place_name", cancellationToken, reader => new
        {
            placeName = reader.GetString(0),
            invitedCount = reader.GetInt32(1),
            satCount = reader.GetInt32(2),
            medianSeconds = reader.GetInt32(3),
            notStartedCount = reader.GetInt32(4),
            targetPercent = reader.GetDecimal(5)
        }).ConfigureAwait(false);
        var agreements = await Read(connection, "SELECT skill_code, skill_name, sample_count, exact_percent, adjacent_percent, weighted_agreement FROM assessment.mark_agreement ORDER BY skill_code", cancellationToken, reader => new
        {
            skillCode = reader.GetString(0),
            skillName = reader.GetString(1),
            sampleCount = reader.GetInt32(2),
            exactPercent = reader.GetDecimal(3),
            adjacentPercent = reader.GetDecimal(4),
            weightedAgreement = reader.GetDecimal(5)
        }).ConfigureAwait(false);
        var bankDepth = await Read(connection, "SELECT skill_code, population_band, typed_percent, spread_l1, spread_l2, spread_l3, spread_l4, spread_l5, sme_name, state FROM assessment.bank_depth ORDER BY skill_code", cancellationToken, reader => new
        {
            skillCode = reader.GetString(0),
            populationBand = reader.GetString(1),
            typedPercent = reader.GetDecimal(2),
            spread = new[] { reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7) },
            smeName = reader.GetString(8),
            state = reader.GetString(9)
        }).ConfigureAwait(false);
        var bankPlans = await Read(connection, "SELECT skill_code, current_count, target_depth, author_name, reviewer_name, due_on, phase FROM assessment.bank_plan ORDER BY due_on, skill_code", cancellationToken, reader => new
        {
            skillCode = reader.GetString(0),
            currentCount = reader.GetInt32(1),
            targetDepth = reader.GetInt32(2),
            authorName = reader.GetString(3),
            reviewerName = reader.GetString(4),
            dueOn = reader.GetDateTime(5).ToString("yyyy-MM-dd"),
            phase = reader.GetString(6)
        }).ConfigureAwait(false);
        var rollingGroups = await Read(connection, "SELECT talent_group, joined_count, assessed_count, plan_count, waiting_count FROM assessment.rolling_group ORDER BY talent_group", cancellationToken, reader => new
        {
            talentGroup = reader.GetString(0),
            joinedCount = reader.GetInt32(1),
            assessedCount = reader.GetInt32(2),
            planCount = reader.GetInt32(3),
            waitingCount = reader.GetInt32(4)
        }).ConfigureAwait(false);
        var rollingSteps = await Read(connection, "SELECT sort_order, title, detail FROM assessment.rolling_step ORDER BY sort_order", cancellationToken, reader => new
        {
            sortOrder = reader.GetInt32(0),
            title = reader.GetString(1),
            detail = reader.GetString(2)
        }).ConfigureAwait(false);
        var blockers = await Read(connection, "SELECT title, detail, state_label FROM assessment.studio_blocker ORDER BY sort_order", cancellationToken, reader => new
        {
            title = reader.GetString(0),
            detail = reader.GetString(1),
            stateLabel = reader.GetString(2)
        }).ConfigureAwait(false);
        var experts = await Read(connection,
            """
            SELECT sme.sme_id, sme.domain_name, sme.expert_name, sme.credentials,
                   COALESCE(coverage.skills_owned, ''), COALESCE(coverage.authored_count, 0), COALESCE(coverage.reviewed_count, 0), coverage.signed_on
            FROM assessment.sme AS sme
            LEFT JOIN assessment.sme_coverage AS coverage ON coverage.sme_id = sme.sme_id
            ORDER BY sme.domain_name
            """,
            cancellationToken,
            reader => new
            {
                smeId = reader.GetGuid(0),
                domainName = reader.GetString(1),
                expertName = reader.GetString(2),
                credentials = reader.GetString(3),
                skillsOwned = reader.GetString(4),
                authoredCount = reader.GetInt32(5),
                reviewedCount = reader.GetInt32(6),
                signedOn = reader.IsDBNull(7) ? null : reader.GetDateTime(7).ToString("yyyy-MM-dd")
            }).ConfigureAwait(false);
        var criteria = await Read(connection,
            """
            SELECT criterion.item_id, item.skill_code, item.prompt, criterion.criterion_name, criterion.weight_percent,
                   criterion.description, criterion.machine_score, criterion.human_score
            FROM assessment.rubric_criterion AS criterion
            JOIN assessment.item AS item ON item.item_id = criterion.item_id
            ORDER BY item.skill_code, criterion.criterion_name
            """,
            cancellationToken,
            reader => new
            {
                itemId = reader.GetGuid(0),
                skillCode = reader.GetString(1),
                prompt = reader.GetString(2),
                criterionName = reader.GetString(3),
                weightPercent = reader.GetInt32(4),
                description = reader.GetString(5),
                machineScore = reader.GetInt32(6),
                humanScore = reader.GetInt32(7)
            }).ConfigureAwait(false);
        var records = await Read(connection,
            """
            SELECT record.item_id, record.anchor_skill, record.target_level, record.item_type, record.rubric_reference,
                   record.author_name, record.reviewer_name, record.version_label, record.status_label
            FROM assessment.item_record AS record
            ORDER BY record.anchor_skill
            """,
            cancellationToken,
            reader => new
            {
                itemId = reader.GetGuid(0),
                anchorSkill = reader.GetString(1),
                targetLevel = reader.GetString(2),
                itemType = reader.GetString(3),
                rubricReference = reader.GetString(4),
                authorName = reader.GetString(5),
                reviewerName = reader.GetString(6),
                versionLabel = reader.GetString(7),
                statusLabel = reader.GetString(8)
            }).ConfigureAwait(false);
        var versions = await Read(connection,
            "SELECT item_id, version_label, recorded_on, note FROM assessment.item_version ORDER BY recorded_on, version_label",
            cancellationToken,
            reader => new
            {
                itemId = reader.GetGuid(0),
                versionLabel = reader.GetString(1),
                recordedOn = reader.GetDateTime(2).ToString("yyyy-MM-dd"),
                note = reader.GetString(3)
            }).ConfigureAwait(false);

        int completed = 0, open = 0, duration = 0;
        await using (var command = new NpgsqlCommand(
            """
            SELECT completed_count, open_count, duration_seconds
            FROM assessment.programme_snapshot
            WHERE kind = 'operations'
            ORDER BY as_of DESC
            LIMIT 1
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                completed = reader.GetInt32(0);
                open = reader.GetInt32(1);
                duration = reader.GetInt32(2);
            }
        }

        var invited = geography.Sum(row => row.invitedCount);
        var sat = geography.Sum(row => row.satCount);
        var notStarted = geography.Sum(row => row.notStartedCount);
        var plans = rollingGroups.Sum(row => row.planCount);
        var liveBanks = bankDepth.Count(row => row.state == "live");
        var questions = new object[]
        {
            new { question = "People assessed", answer = $"{completed} completed sittings, {open} still open. Geography records {sat} of {invited} invited.", stateLabel = open > 0 ? "Open" : "Recorded" },
            new { question = "Completion rate", answer = invited == 0 ? "No geography row is stored." : $"{Percent(sat, invited)} of invited people in the geography rows, against the stored target.", stateLabel = "Stored" },
            new { question = "Anchor skills with a published baseline", answer = $"{liveBanks} of {bankDepth.Count} bank rows are live.", stateLabel = "Stored" },
            new { question = "Individuals issued a plan", answer = $"{plans} plans on the rolling-intake rows.", stateLabel = plans > 0 ? "Issued" : "None stored" }
        };
        var held = itemStatistics.Where(row => row.disposition == "held").Select(row => row.skillCode).ToArray();
        return new
        {
            exposure,
            selection,
            stopping,
            itemStatistics,
            geography,
            agreements,
            bankDepth,
            bankPlans,
            rollingGroups,
            rollingSteps,
            blockers,
            experts,
            criteria,
            records,
            versions,
            scorecard = new { completed, open, durationSeconds = duration, invited, sat, notStarted, questions },
            heldNote = held.Length == 0 ? "No stored item statistic is held back." : "Held back: " + string.Join(", ", held) + "."
        };
    }

    private static string Percent(int part, int whole) => whole == 0 ? "0%" : $"{Math.Round(100.0 * part / whole, 1)}%";

    private static async Task<List<T>> Read<T>(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, Func<NpgsqlDataReader, T> map)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(map(reader));
        return rows;
    }
}
