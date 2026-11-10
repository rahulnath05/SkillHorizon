CREATE TABLE activation.capacity (
    zone_id text PRIMARY KEY,
    sitting_budget integer NOT NULL,
    CONSTRAINT capacity_budget_positive CHECK (sitting_budget > 0)
);

CREATE TABLE activation.wave (
    wave_id uuid PRIMARY KEY,
    benchmark_version_id uuid NOT NULL,
    population integer NOT NULL,
    created_at timestamptz NOT NULL,
    CONSTRAINT wave_population_positive CHECK (population > 0)
);

CREATE TABLE activation.admission (
    wave_id uuid NOT NULL REFERENCES activation.wave (wave_id),
    subject_id text NOT NULL,
    PRIMARY KEY (wave_id, subject_id)
);

CREATE TABLE activation.sitting_authorization (
    authorization_id uuid PRIMARY KEY,
    token_hash text NOT NULL,
    subject_id text NOT NULL,
    wave_id uuid NOT NULL REFERENCES activation.wave (wave_id),
    benchmark_version_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    CONSTRAINT sitting_authorization_token_unique UNIQUE (token_hash)
);

CREATE TABLE readiness.sealed_result (
    result_id uuid PRIMARY KEY,
    authorization_id uuid NOT NULL,
    subject_id text NOT NULL,
    benchmark_version_id uuid NOT NULL,
    wave_id uuid NOT NULL,
    sealed_at timestamptz NOT NULL,
    CONSTRAINT sealed_result_authorization_unique UNIQUE (authorization_id)
);

CREATE TABLE readiness.proficiency (
    result_id uuid NOT NULL REFERENCES readiness.sealed_result (result_id),
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    proficiency_rank integer NOT NULL,
    proficiency_code text NOT NULL,
    proficiency_name text NOT NULL,
    PRIMARY KEY (result_id, skill_code)
);

CREATE FUNCTION readiness.guard_sealed() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'sealed results are immutable';
END;
$$;

CREATE TRIGGER readiness_result_guard
    BEFORE UPDATE OR DELETE ON readiness.sealed_result
    FOR EACH ROW EXECUTE FUNCTION readiness.guard_sealed();

CREATE TRIGGER readiness_proficiency_guard
    BEFORE UPDATE OR DELETE ON readiness.proficiency
    FOR EACH ROW EXECUTE FUNCTION readiness.guard_sealed();
