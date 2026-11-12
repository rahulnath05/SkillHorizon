CREATE TABLE assessment.engine_exposure (
    exposure_id uuid PRIMARY KEY,
    skill_code text NOT NULL,
    item_label text NOT NULL,
    exposed_count integer NOT NULL,
    ceiling_percent numeric NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT exposure_counts_known CHECK (exposed_count >= 0 AND ceiling_percent >= 0)
);

CREATE TABLE assessment.engine_selection (
    selection_id uuid PRIMARY KEY,
    sitting_label text NOT NULL,
    step_number integer NOT NULL,
    skill_code text NOT NULL,
    rule_name text NOT NULL,
    estimate numeric NOT NULL,
    band numeric NOT NULL,
    outcome text NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT selection_step_positive CHECK (step_number > 0),
    CONSTRAINT selection_outcome_known CHECK (outcome IN ('credited', 'not-credited'))
);

CREATE TABLE assessment.engine_stopping (
    rule_id uuid PRIMARY KEY,
    rule_name text NOT NULL,
    detail text NOT NULL,
    stop_band numeric NOT NULL,
    item_cap integer NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT stopping_cap_positive CHECK (item_cap > 0)
);

CREATE TABLE assessment.item_statistic (
    statistic_id uuid PRIMARY KEY,
    skill_code text NOT NULL,
    item_count integer NOT NULL,
    difficulty numeric NOT NULL,
    discrimination numeric NOT NULL,
    median_seconds integer NOT NULL,
    disposition text NOT NULL,
    CONSTRAINT item_statistic_skill_unique UNIQUE (skill_code),
    CONSTRAINT item_statistic_disposition CHECK (disposition IN ('passed', 'held')),
    CONSTRAINT item_statistic_counts CHECK (item_count >= 0 AND median_seconds >= 0)
);

CREATE TABLE assessment.geography_completion (
    geography_id uuid PRIMARY KEY,
    place_name text NOT NULL,
    invited_count integer NOT NULL,
    sat_count integer NOT NULL,
    median_seconds integer NOT NULL,
    not_started_count integer NOT NULL,
    target_percent numeric NOT NULL,
    CONSTRAINT geography_place_unique UNIQUE (place_name),
    CONSTRAINT geography_counts CHECK (
        invited_count >= 0 AND sat_count >= 0 AND median_seconds >= 0 AND not_started_count >= 0 AND target_percent >= 0)
);

CREATE TABLE assessment.mark_agreement (
    agreement_id uuid PRIMARY KEY,
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    sample_count integer NOT NULL,
    exact_percent numeric NOT NULL,
    adjacent_percent numeric NOT NULL,
    weighted_agreement numeric NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT mark_agreement_skill_unique UNIQUE (skill_code),
    CONSTRAINT mark_agreement_sample CHECK (sample_count >= 0)
);

CREATE TABLE assessment.bank_depth (
    skill_code text PRIMARY KEY,
    population_band text NOT NULL,
    typed_percent numeric NOT NULL,
    spread_l1 integer NOT NULL,
    spread_l2 integer NOT NULL,
    spread_l3 integer NOT NULL,
    spread_l4 integer NOT NULL,
    spread_l5 integer NOT NULL,
    sme_name text NOT NULL,
    state text NOT NULL,
    CONSTRAINT bank_depth_state CHECK (state IN ('live', 'review', 'thin')),
    CONSTRAINT bank_depth_spread CHECK (
        spread_l1 >= 0 AND spread_l2 >= 0 AND spread_l3 >= 0 AND spread_l4 >= 0 AND spread_l5 >= 0)
);

CREATE TABLE assessment.bank_plan (
    skill_code text PRIMARY KEY,
    current_count integer NOT NULL,
    target_depth integer NOT NULL,
    author_name text NOT NULL,
    reviewer_name text NOT NULL,
    due_on date NOT NULL,
    phase text NOT NULL,
    CONSTRAINT bank_plan_counts CHECK (current_count >= 0 AND target_depth > 0)
);

CREATE TABLE assessment.rolling_group (
    group_id uuid PRIMARY KEY,
    talent_group text NOT NULL,
    joined_count integer NOT NULL,
    assessed_count integer NOT NULL,
    plan_count integer NOT NULL,
    waiting_count integer NOT NULL,
    CONSTRAINT rolling_group_unique UNIQUE (talent_group),
    CONSTRAINT rolling_group_counts CHECK (
        joined_count >= 0 AND assessed_count >= 0 AND plan_count >= 0 AND waiting_count >= 0)
);

CREATE TABLE assessment.rolling_step (
    step_id uuid PRIMARY KEY,
    sort_order integer NOT NULL,
    title text NOT NULL,
    detail text NOT NULL,
    CONSTRAINT rolling_step_order_unique UNIQUE (sort_order)
);

CREATE TABLE assessment.studio_blocker (
    blocker_id uuid PRIMARY KEY,
    sort_order integer NOT NULL,
    title text NOT NULL,
    detail text NOT NULL,
    state_label text NOT NULL,
    CONSTRAINT studio_blocker_order_unique UNIQUE (sort_order)
);

CREATE TABLE assessment.sme_coverage (
    sme_id uuid PRIMARY KEY REFERENCES assessment.sme (sme_id),
    skills_owned text NOT NULL DEFAULT '',
    authored_count integer NOT NULL DEFAULT 0,
    reviewed_count integer NOT NULL DEFAULT 0,
    signed_on date,
    CONSTRAINT sme_coverage_counts CHECK (authored_count >= 0 AND reviewed_count >= 0)
);

CREATE TABLE assessment.item_record (
    item_id uuid PRIMARY KEY REFERENCES assessment.item (item_id),
    anchor_skill text NOT NULL,
    target_level text NOT NULL,
    item_type text NOT NULL,
    rubric_reference text NOT NULL,
    author_name text NOT NULL,
    reviewer_name text NOT NULL,
    version_label text NOT NULL,
    status_label text NOT NULL
);

CREATE TABLE assessment.item_version (
    item_id uuid NOT NULL REFERENCES assessment.item (item_id),
    version_label text NOT NULL,
    recorded_on date NOT NULL,
    note text NOT NULL,
    PRIMARY KEY (item_id, version_label)
);

CREATE TABLE assessment.rubric_criterion (
    item_id uuid NOT NULL REFERENCES assessment.item (item_id),
    criterion_name text NOT NULL,
    weight_percent integer NOT NULL,
    description text NOT NULL,
    machine_score integer NOT NULL,
    human_score integer NOT NULL,
    PRIMARY KEY (item_id, criterion_name),
    CONSTRAINT rubric_criterion_weight CHECK (weight_percent > 0 AND weight_percent <= 100),
    CONSTRAINT rubric_criterion_scores CHECK (machine_score BETWEEN 1 AND 5 AND human_score BETWEEN 1 AND 5)
);
