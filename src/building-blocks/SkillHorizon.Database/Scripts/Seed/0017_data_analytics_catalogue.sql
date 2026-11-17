-- Data & Analytics catalogue from the learning and development reference.
-- Runs only when no taxonomy is published, so a wiped database is rebuilt and an existing one is left alone.

DO $$
#variable_conflict use_variable
DECLARE
    practice_id uuid := 'b22b0001-0000-4000-8000-000000000001';
    competency_id uuid := 'b22b0001-0000-4000-8000-000000000002';
    version_id uuid := 'b22b0001-0000-4000-8000-000000000020';
    framework_id uuid := 'b22b0001-0000-4000-8000-000000000030';
    associate_level_id uuid := 'b22b0001-0000-4000-8000-000000000031';
    senior_level_id uuid := 'b22b0001-0000-4000-8000-000000000032';
    pack_id uuid := 'b22b0001-0000-4000-8000-000000000200';
    senior_profile_id uuid;
    senior_benchmark_id uuid;
    draft_id uuid := 'b22b0001-0000-4000-8000-000000000220';
    profile_row record;
    profile_id uuid;
    benchmark_id uuid;
    row_id uuid;
    pair text;
    skill_code text;
    skill_rank integer;
    classification text;
    exception_note text;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('data-analytics-catalogue'));

    IF EXISTS (SELECT 1 FROM taxonomy.version WHERE status = 'published') THEN
        RETURN;
    END IF;

    INSERT INTO taxonomy.practice (practice_id, code, display_name)
    VALUES (practice_id, 'data', 'Data');

    INSERT INTO taxonomy.competency (
        competency_id, practice_id, code, display_name, com_lead, approval_status)
    VALUES (
        competency_id, practice_id, 'data-analytics', 'Data & Analytics', 'person-lead', 'changes-requested');

    INSERT INTO taxonomy.skill (skill_id, competency_id, code, display_name, category, definition, status)
    VALUES
        ('b22b0001-0000-4000-8000-000000000101', competency_id, 'd-01', 'Data architecture & modelling', 'Data foundations', 'Designs models, grain and keys that downstream teams can rely on.', 'active'),
        ('b22b0001-0000-4000-8000-000000000102', competency_id, 'd-02', 'Databricks / Snowflake platforms', 'Data platform', 'Builds and tunes workloads on Databricks or Snowflake.', 'active'),
        ('b22b0001-0000-4000-8000-000000000103', competency_id, 'd-03', 'Streaming & real-time pipelines', 'Data platform', 'Designs streaming and real-time pipelines, including late and out-of-order data.', 'active'),
        ('b22b0001-0000-4000-8000-000000000104', competency_id, 'd-04', 'Data governance & quality', 'Data foundations', 'Sets and enforces the rules that make data trustworthy, and proves they hold.', 'active'),
        ('b22b0001-0000-4000-8000-000000000105', competency_id, 'd-05', 'Analytics engineering (dbt/SQL)', 'Analytics engineering', 'Turns raw data into governed, tested, documented models in dbt and SQL.', 'active'),
        ('b22b0001-0000-4000-8000-000000000106', competency_id, 'd-06', 'Data visualisation & storytelling', 'Analytics engineering', 'Turns a result into a decision the audience can act on.', 'active'),
        ('b22b0001-0000-4000-8000-000000000107', competency_id, 'e-01', 'Python / Java / TypeScript', 'Engineering craft', 'Writes production code in Python, Java or TypeScript that others can maintain.', 'active');

    INSERT INTO taxonomy.version (taxonomy_version_id, version_number, status, published_at, created_at)
    VALUES (version_id, 1, 'published', now(), now());

    INSERT INTO taxonomy.version_skill (
        taxonomy_version_id, skill_id, skill_code, skill_name, competency_code, competency_name,
        practice_code, practice_name, category, definition, status)
    SELECT version_id, skill.skill_id, skill.code, skill.display_name, 'data-analytics', 'Data & Analytics',
           'data', 'Data', skill.category, skill.definition, skill.status
    FROM taxonomy.skill AS skill
    WHERE skill.competency_id = competency_id;

    INSERT INTO taxonomy.proficiency (taxonomy_version_id, code, display_name, rank, meaning)
    VALUES
        (version_id, 'awareness', 'Awareness', 1, 'Explains core concepts and completes defined tasks with close guidance.'),
        (version_id, 'working', 'Working', 2, 'Applies the skill independently in familiar, well-scoped situations.'),
        (version_id, 'practitioner', 'Practitioner', 3, 'Owns varied work, resolves common ambiguity, and produces reliable outcomes.'),
        (version_id, 'advanced', 'Advanced', 4, 'Designs approaches, governs quality, and guides others through complex work.'),
        (version_id, 'authority', 'Authority', 5, 'Shapes enterprise standards, strategy, and external market practice.');

    INSERT INTO job_architecture.framework (framework_id, code, display_name)
    VALUES (framework_id, 'data', 'Data & Analytics');

    INSERT INTO job_architecture.level (level_id, framework_id, code, display_name, rank)
    VALUES
        (associate_level_id, framework_id, 'associate', 'Associate', 1),
        (senior_level_id, framework_id, 'senior', 'Senior', 2);

    INSERT INTO attestation.pack (pack_id, practice_code, status, submitted_by, submitted_at)
    VALUES (pack_id, 'data', 'submitted', 'person-ld', now());

    FOR profile_row IN
        SELECT *
        FROM (VALUES
            ('data-analyst', 'Data Analyst', 'Associate', 'Builds trusted analysis and operational dashboards from governed data.', 'd-01:2|d-04:2|d-05:2|d-06:2|e-01:2'),
            ('senior-data-engineer', 'Senior Data Engineer', 'Senior', 'Owns the platform, the models and the contracts that downstream teams depend on.', 'd-01:4|d-02:4|d-03:3|d-04:3|d-05:4|e-01:3'),
            ('data-pack-bi-associate', 'BI Associate', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-junior-analytics-engineer', 'Junior Analytics Engineer', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-product-analyst-i', 'Product Analyst I', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-marketing-analyst', 'Marketing Analyst', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-financial-data-analyst', 'Financial Data Analyst', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-senior-bi-analyst', 'Senior BI Analyst', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-analytics-engineer-ii', 'Analytics Engineer II', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-bi-analyst', 'BI Analyst', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-analytics-manager', 'Analytics Manager', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-bi-team-lead', 'BI Team Lead', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-data-solution-architect', 'Data Solution Architect', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-principal-analyst', 'Principal Analyst', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-bi-architect', 'BI Architect', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-sr-analytics-manager', 'Sr Analytics Manager', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3'),
            ('data-pack-director-of-analytics', 'Director of Analytics', 'Associate', 'Benchmark targets approved in the prior competency cycle.', 'd-05:3')
        ) AS profile(code, name, band, purpose, skills)
    LOOP
        profile_id := (
            'b22b0002-' || substr(md5(profile_row.code), 1, 4) || '-4000-8000-' || substr(md5(profile_row.code), 5, 12)
        )::uuid;
        benchmark_id := (
            'b22b0003-' || substr(md5(profile_row.code), 1, 4) || '-4000-8000-' || substr(md5(profile_row.code), 5, 12)
        )::uuid;
        row_id := (
            'b22b0004-' || substr(md5(profile_row.code), 1, 4) || '-4000-8000-' || substr(md5(profile_row.code), 5, 12)
        )::uuid;

        INSERT INTO job_architecture.job_profile (
            profile_id, framework_id, level_id, code, display_name, purpose, band, family_name, competency_code)
        VALUES (
            profile_id,
            framework_id,
            CASE WHEN profile_row.band = 'Senior' THEN senior_level_id ELSE associate_level_id END,
            profile_row.code,
            profile_row.name,
            profile_row.purpose,
            profile_row.band,
            'Data',
            'data-analytics');

        INSERT INTO benchmark.version (
            benchmark_version_id, job_profile_id, taxonomy_version_id, version_number, status, published_at, created_at)
        VALUES (benchmark_id, profile_id, version_id, 1, 'published', now(), now());

        FOREACH pair IN ARRAY string_to_array(profile_row.skills, '|')
        LOOP
            skill_code := split_part(pair, ':', 1);
            skill_rank := split_part(pair, ':', 2)::integer;
            INSERT INTO benchmark.target (
                benchmark_version_id, skill_id, skill_code, skill_name, competency_name, practice_name,
                proficiency_rank, proficiency_code, proficiency_name, suggested_rank, level_set)
            SELECT benchmark_id, skill.skill_id, skill.code, skill.display_name, 'Data & Analytics', 'Data',
                   proficiency.rank, proficiency.code, proficiency.display_name, proficiency.rank, true
            FROM taxonomy.skill AS skill
            JOIN taxonomy.proficiency AS proficiency
                ON proficiency.taxonomy_version_id = version_id
               AND proficiency.rank = skill_rank
            WHERE skill.code = skill_code;
        END LOOP;

        IF profile_row.code = 'senior-data-engineer' THEN
            senior_profile_id := profile_id;
            senior_benchmark_id := benchmark_id;
            classification := 'new_bar';
            exception_note := 'Streaming & real-time pipelines is proposed at Advanced, above the published Practitioner bar.';
        ELSE
            classification := 'aligned';
            exception_note := NULL;
        END IF;

        INSERT INTO attestation.pack_row (
            row_id, pack_id, job_profile_id, profile_code, profile_name, classification, exception_note)
        VALUES (row_id, pack_id, profile_id, profile_row.code, profile_row.name, classification, exception_note);
    END LOOP;

    INSERT INTO benchmark.version (
        benchmark_version_id, job_profile_id, taxonomy_version_id, version_number, status, created_at)
    VALUES (draft_id, senior_profile_id, version_id, 2, 'draft', now());

    INSERT INTO benchmark.target (
        benchmark_version_id, skill_id, skill_code, skill_name, competency_name, practice_name,
        proficiency_rank, proficiency_code, proficiency_name, suggested_rank, level_set)
    SELECT draft_id, skill_id, skill_code, skill_name, competency_name, practice_name,
           proficiency_rank, proficiency_code, proficiency_name, suggested_rank, level_set
    FROM benchmark.target
    WHERE benchmark_version_id = senior_benchmark_id;

    UPDATE benchmark.target AS target
    SET proficiency_rank = proficiency.rank,
        proficiency_code = proficiency.code,
        proficiency_name = proficiency.display_name,
        suggested_rank = proficiency.rank
    FROM taxonomy.proficiency AS proficiency
    WHERE target.benchmark_version_id = draft_id
      AND target.skill_code = 'd-03'
      AND proficiency.taxonomy_version_id = version_id
      AND proficiency.rank = 4;

    INSERT INTO workforce.profile_correction (
        request_id, manager_employee_id, report_employee_id, note, recorded_at,
        title, evidence, request_status, competency_code)
    VALUES (
        'b22b0001-0000-4000-8000-000000000300',
        'person-lead',
        'person-lead',
        'Three programmes in the pipeline need real-time pipelines. We staff them from Analytics Engineering and the gap shows up in delivery.',
        now(),
        'Streaming Data Engineer',
        '3 programmes · 9 people over two quarters · scope and band to confirm',
        'needs-context',
        'data-analytics');

    INSERT INTO access.practice_scope (subject_id, practice_code, granted_at)
    VALUES ('person-lead', 'data', now())
    ON CONFLICT (subject_id, practice_code) DO NOTHING;

    INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
    VALUES
        ('11111111-1111-1111-1111-111111111111', 'person-multi', 'employee', now()),
        ('22222222-2222-2222-2222-222222222222', 'person-multi', 'people_manager', now()),
        ('33333333-3333-3333-3333-333333333333', 'person-single', 'employee', now()),
        ('44444444-4444-4444-4444-444444444444', 'person-ld', 'learning_and_development', now()),
        ('55555555-5555-5555-5555-555555555555', 'person-lead', 'competency_lead', now()),
        ('66666666-6666-6666-6666-666666666666', 'person-admin', 'platform_admin', now())
    ON CONFLICT (subject_id, workspace) DO NOTHING;
END
$$;
