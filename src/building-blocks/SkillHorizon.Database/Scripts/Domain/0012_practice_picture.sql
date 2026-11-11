CREATE TABLE readiness.heatmap (
    practice_code text PRIMARY KEY,
    as_of timestamptz NOT NULL
);

CREATE TABLE readiness.heatmap_cell (
    practice_code text NOT NULL REFERENCES readiness.heatmap (practice_code) ON DELETE CASCADE,
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    level_code text NOT NULL,
    level_name text NOT NULL,
    level_rank integer NOT NULL,
    proficiency_rank integer NOT NULL,
    proficiency_name text NOT NULL,
    PRIMARY KEY (practice_code, skill_code, level_code),
    CONSTRAINT heatmap_level_rank_positive CHECK (level_rank > 0),
    CONSTRAINT heatmap_proficiency_rank_positive CHECK (proficiency_rank > 0)
);

CREATE TABLE readiness.progression (
    practice_code text PRIMARY KEY,
    as_of timestamptz NOT NULL,
    path_count integer NOT NULL,
    step_count integer NOT NULL,
    open_step_count integer NOT NULL,
    CONSTRAINT progression_counts_non_negative CHECK (path_count >= 0 AND step_count >= 0 AND open_step_count >= 0)
);

CREATE TABLE readiness.cut (
    practice_code text NOT NULL,
    cut_kind text NOT NULL,
    cut_key text NOT NULL,
    as_of timestamptz NOT NULL,
    member_count integer NOT NULL,
    measured_count integer NOT NULL,
    gap_count integer NOT NULL,
    PRIMARY KEY (practice_code, cut_kind, cut_key),
    CONSTRAINT cut_kind_known CHECK (cut_kind IN ('country', 'talent_group')),
    CONSTRAINT cut_counts_non_negative CHECK (member_count >= 0 AND measured_count >= 0 AND gap_count >= 0)
);

CREATE SCHEMA market;

CREATE TABLE market.demand (
    practice_code text PRIMARY KEY,
    note text NOT NULL,
    posting_count integer NOT NULL,
    role_count integer NOT NULL,
    status text NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT market_status_known CHECK (status IN ('draft', 'published')),
    CONSTRAINT market_note_length CHECK (char_length(btrim(note)) BETWEEN 1 AND 500),
    CONSTRAINT market_counts_non_negative CHECK (posting_count >= 0 AND role_count >= 0)
);

CREATE TABLE reference.market_demand (
    practice_code text PRIMARY KEY,
    note text NOT NULL,
    posting_count integer NOT NULL,
    role_count integer NOT NULL,
    published_at timestamptz NOT NULL,
    CONSTRAINT reference_market_note_length CHECK (char_length(btrim(note)) BETWEEN 1 AND 500),
    CONSTRAINT reference_market_counts_non_negative CHECK (posting_count >= 0 AND role_count >= 0)
);
