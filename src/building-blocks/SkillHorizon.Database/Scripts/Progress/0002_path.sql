CREATE TABLE progress.path (
    path_id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    result_id uuid NOT NULL,
    benchmark_version_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    CONSTRAINT path_result_benchmark_unique UNIQUE (result_id, benchmark_version_id)
);

ALTER TABLE progress.step
    ADD COLUMN path_id uuid REFERENCES progress.path (path_id),
    ADD COLUMN skill_code text;

CREATE TABLE progress.evidence (
    evidence_id uuid PRIMARY KEY,
    step_id uuid NOT NULL REFERENCES progress.step (step_id),
    object_key text NOT NULL,
    file_name text NOT NULL,
    content_type text NOT NULL,
    byte_length integer NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT evidence_length_positive CHECK (byte_length > 0)
);

CREATE TABLE progress.validation (
    validation_id uuid PRIMARY KEY,
    step_id uuid NOT NULL REFERENCES progress.step (step_id),
    manager_employee_id text NOT NULL,
    report_employee_id text NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT validation_step_unique UNIQUE (step_id)
);

CREATE TABLE progress.audit_record (
    audit_id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    action text NOT NULL,
    record_type text NOT NULL,
    record_id uuid NOT NULL,
    correlation_id text NOT NULL,
    detail jsonb NOT NULL,
    occurred_at timestamptz NOT NULL
);
