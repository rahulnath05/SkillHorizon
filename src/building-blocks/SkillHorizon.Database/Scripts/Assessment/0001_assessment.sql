CREATE SCHEMA IF NOT EXISTS assessment;

CREATE TABLE assessment.item (
    item_id uuid PRIMARY KEY,
    skill_code text NOT NULL,
    prompt text NOT NULL,
    CONSTRAINT item_skill_unique UNIQUE (skill_code)
);

CREATE TABLE assessment.choice (
    item_id uuid NOT NULL REFERENCES assessment.item (item_id),
    rank integer NOT NULL,
    label text NOT NULL,
    PRIMARY KEY (item_id, rank),
    CONSTRAINT choice_rank_positive CHECK (rank > 0)
);

CREATE TABLE assessment.sitting (
    sitting_id uuid PRIMARY KEY,
    authorization_id uuid NOT NULL,
    token_hash text NOT NULL,
    subject_id text NOT NULL,
    wave_id uuid NOT NULL,
    benchmark_version_id uuid NOT NULL,
    scale jsonb NOT NULL,
    status text NOT NULL,
    opened_at timestamptz NOT NULL,
    sealed_at timestamptz,
    CONSTRAINT sitting_status_known CHECK (status IN ('open', 'sealed')),
    CONSTRAINT sitting_open_per_wave UNIQUE (subject_id, wave_id)
);

CREATE TABLE assessment.response (
    sitting_id uuid NOT NULL REFERENCES assessment.sitting (sitting_id),
    item_id uuid NOT NULL,
    skill_code text NOT NULL,
    selected_rank integer NOT NULL,
    responded_at timestamptz NOT NULL,
    PRIMARY KEY (sitting_id, item_id)
);

CREATE TABLE assessment.sealed_result (
    result_id uuid PRIMARY KEY,
    sitting_id uuid NOT NULL,
    subject_id text NOT NULL,
    benchmark_version_id uuid NOT NULL,
    sealed_at timestamptz NOT NULL,
    CONSTRAINT assessment_result_sitting_unique UNIQUE (sitting_id)
);

CREATE TABLE assessment.sealed_proficiency (
    result_id uuid NOT NULL REFERENCES assessment.sealed_result (result_id),
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    proficiency_rank integer NOT NULL,
    proficiency_code text NOT NULL,
    proficiency_name text NOT NULL,
    PRIMARY KEY (result_id, skill_code)
);

CREATE FUNCTION assessment.guard_response() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    parent_status text;
BEGIN
    SELECT status INTO parent_status FROM assessment.sitting WHERE sitting_id = OLD.sitting_id;
    IF parent_status IS DISTINCT FROM 'open' THEN
        RAISE EXCEPTION 'sealed responses are immutable';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER assessment_response_guard
    BEFORE UPDATE OR DELETE ON assessment.response
    FOR EACH ROW EXECUTE FUNCTION assessment.guard_response();
