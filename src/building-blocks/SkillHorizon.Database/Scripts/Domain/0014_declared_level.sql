CREATE TABLE readiness.declaration (
    declaration_id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    skill_code text NOT NULL,
    declared_rank integer NOT NULL,
    declared_name text NOT NULL,
    declared_at timestamptz NOT NULL,
    CONSTRAINT declaration_subject_skill UNIQUE (subject_id, skill_code),
    CONSTRAINT declaration_rank_positive CHECK (declared_rank > 0),
    CONSTRAINT declaration_name_present CHECK (length(btrim(declared_name)) > 0)
);
