using Npgsql;

namespace SkillHorizon.Learning;

public sealed class StudioMeasureFixture
{
    private readonly NpgsqlDataSource _assessment;

    public StudioMeasureFixture(NpgsqlDataSource assessment)
    {
        _assessment = assessment;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _assessment.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (await Exists(connection, "SELECT 1 FROM assessment.engine_stopping LIMIT 1", cancellationToken).ConfigureAwait(false))
            return;

        await Execute(connection, """
            INSERT INTO assessment.engine_exposure (exposure_id, skill_code, item_label, exposed_count, ceiling_percent, recorded_at)
            SELECT gen_random_uuid(), skill_code, 'Stored exposure for ' || skill_code, count(*)::int, 15, now()
            FROM assessment.item
            GROUP BY skill_code
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.engine_selection (
                selection_id, sitting_label, step_number, skill_code, rule_name, estimate, band, outcome, recorded_at)
            VALUES
                (gen_random_uuid(), 'sql-sitting', 1, 'sql', 'Open near the benchmark', 3.0, 0.80, 'credited', now()),
                (gen_random_uuid(), 'sql-sitting', 2, 'sql', 'Next item closest to the estimate', 3.4, 0.55, 'not-credited', now()),
                (gen_random_uuid(), 'sql-sitting', 3, 'sql', 'Easier item after a miss', 3.1, 0.28, 'credited', now())
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.engine_stopping (rule_id, rule_name, detail, stop_band, item_cap, recorded_at)
            VALUES (gen_random_uuid(), 'Band or cap', 'Stop when the band is within 0.30 or 12 items have been used.', 0.30, 12, now())
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.item_statistic (
                statistic_id, skill_code, item_count, difficulty, discrimination, median_seconds, disposition)
            SELECT gen_random_uuid(), skill_code, count(*)::int,
                   round(avg(difficulty), 2),
                   CASE skill_code WHEN 'modeling' THEN 0.24 ELSE 0.46 END,
                   70,
                   CASE skill_code WHEN 'modeling' THEN 'held' ELSE 'passed' END
            FROM assessment.item
            GROUP BY skill_code
            ON CONFLICT (skill_code) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.geography_completion (
                geography_id, place_name, invited_count, sat_count, median_seconds, not_started_count, target_percent)
            VALUES
                (gen_random_uuid(), 'India', 2, 1, 1200, 1, 85),
                (gen_random_uuid(), 'Poland', 1, 0, 0, 1, 85)
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.mark_agreement (
                agreement_id, skill_code, skill_name, sample_count, exact_percent, adjacent_percent, weighted_agreement, recorded_at)
            VALUES
                (gen_random_uuid(), 'sql', 'SQL', 1, 100, 100, 0.74, now()),
                (gen_random_uuid(), 'modeling', 'Modeling', 1, 0, 100, 0.61, now())
            ON CONFLICT (skill_code) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.bank_depth (
                skill_code, population_band, typed_percent, spread_l1, spread_l2, spread_l3, spread_l4, spread_l5, sme_name, state)
            SELECT item.skill_code, 'Local',
                   round(100.0 * count(rubric.item_id) / count(*), 1),
                   count(*) FILTER (WHERE item.difficulty = 1)::int,
                   count(*) FILTER (WHERE item.difficulty = 2)::int,
                   count(*) FILTER (WHERE item.difficulty = 3)::int,
                   count(*) FILTER (WHERE item.difficulty = 4)::int,
                   count(*) FILTER (WHERE item.difficulty = 5)::int,
                   'Named on the panel',
                   'live'
            FROM assessment.item AS item
            LEFT JOIN assessment.rubric AS rubric ON rubric.item_id = item.item_id
            GROUP BY item.skill_code
            ON CONFLICT (skill_code) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.bank_depth (
                skill_code, population_band, typed_percent, spread_l1, spread_l2, spread_l3, spread_l4, spread_l5, sme_name, state)
            VALUES ('storytelling', 'None', 0, 0, 0, 0, 0, 0, 'Unassigned', 'thin')
            ON CONFLICT (skill_code) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.bank_plan (skill_code, current_count, target_depth, author_name, reviewer_name, due_on, phase)
            VALUES ('storytelling', 0, 8, 'Ananya Rao', 'Sunita Varma', (timezone('utc', now()))::date + 45, 'No items stored')
            ON CONFLICT (skill_code) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.rolling_group (group_id, talent_group, joined_count, assessed_count, plan_count, waiting_count)
            VALUES (gen_random_uuid(), 'Data', 2, 1, 0, 1)
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.rolling_step (step_id, sort_order, title, detail) VALUES
                (gen_random_uuid(), 1, 'Start date arrives', 'The HR feed carries the joining date. Nobody has to add the person by hand.'),
                (gen_random_uuid(), 2, 'Anchor skills assigned', 'The same profile mapping the wave uses is applied. There is no self-declaration.'),
                (gen_random_uuid(), 3, 'Invited inside the window', 'A sitting is opened against the person calendar, not the original wave window.'),
                (gen_random_uuid(), 4, 'Plan issued on completion', 'A completed sitting can issue a plan without waiting for the next wave.')
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.studio_blocker (blocker_id, sort_order, title, detail, state_label)
            SELECT gen_random_uuid(), 1,
                   'One anchor skill has no items',
                   'Storytelling is marked too thin. ' || count(*)::text || ' items are stored on the other skills.',
                   'Plan set'
            FROM assessment.item
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.sme_coverage (sme_id, skills_owned, authored_count, reviewed_count, signed_on)
            SELECT sme_id,
                   CASE domain_name
                       WHEN 'Data' THEN 'sql, modeling'
                       WHEN 'Cross-group' THEN 'rubric standard'
                       ELSE domain_name
                   END,
                   CASE domain_name WHEN 'Data' THEN 2 ELSE 0 END,
                   CASE domain_name WHEN 'Cross-group' THEN 1 ELSE 0 END,
                   CASE domain_name WHEN 'Cloud' THEN DATE '2026-08-27' ELSE (timezone('utc', now()))::date END
            FROM assessment.sme
            ON CONFLICT (sme_id) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.rubric_criterion (
                item_id, criterion_name, weight_percent, description, machine_score, human_score)
            SELECT item.item_id, criterion.name, 25, criterion.description, criterion.machine_score, criterion.human_score
            FROM assessment.item AS item
            CROSS JOIN (VALUES
                ('Order', 'Names the sequence of the work.', 3, 3),
                ('Owner', 'Names who must be told.', 3, 2),
                ('Window', 'Holds the change to an agreed window.', 2, 2),
                ('Read path', 'Keeps the previous figure readable.', 3, 3)
            ) AS criterion(name, description, machine_score, human_score)
            WHERE item.skill_code = 'sql' AND EXISTS (SELECT 1 FROM assessment.rubric AS rubric WHERE rubric.item_id = item.item_id)
            ON CONFLICT (item_id, criterion_name) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.item_record (
                item_id, anchor_skill, target_level, item_type, rubric_reference, author_name, reviewer_name, version_label, status_label)
            SELECT item.item_id, item.skill_code, 'L' || item.difficulty::text,
                   CASE WHEN rubric.item_id IS NULL THEN 'Selected answer' ELSE 'Typed answer' END,
                   COALESCE(left(rubric.body, 40), 'choices'),
                   'Ananya Rao', 'Sunita Varma', 'v1', 'Live'
            FROM assessment.item AS item
            LEFT JOIN assessment.rubric AS rubric ON rubric.item_id = item.item_id
            ON CONFLICT (item_id) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);

        await Execute(connection, """
            INSERT INTO assessment.item_version (item_id, version_label, recorded_on, note)
            SELECT item_id, 'v1', (timezone('utc', now()))::date, 'First stored version of the item.'
            FROM assessment.item
            ON CONFLICT (item_id, version_label) DO NOTHING
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> Exists(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }
}
