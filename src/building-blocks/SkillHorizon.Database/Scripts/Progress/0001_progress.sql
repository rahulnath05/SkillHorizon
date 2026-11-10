CREATE SCHEMA IF NOT EXISTS progress;

CREATE TABLE progress.step (
    step_id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    provider text NOT NULL,
    title text NOT NULL,
    benchmark_version_id uuid,
    result_id uuid,
    status text NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT step_provider_known CHECK (provider IN ('skillhorizon_assessment', 'external')),
    CONSTRAINT step_status_known CHECK (status IN ('assigned', 'completed')),
    CONSTRAINT step_result_unique UNIQUE (result_id)
);
