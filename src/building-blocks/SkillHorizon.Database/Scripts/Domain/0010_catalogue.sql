CREATE SCHEMA catalogue;

CREATE TABLE catalogue.profile_opening (
    request_id uuid PRIMARY KEY REFERENCES workforce.profile_correction (request_id),
    profile_id uuid NOT NULL REFERENCES job_architecture.job_profile (profile_id),
    opened_at timestamptz NOT NULL
);

CREATE TABLE catalogue.submission (
    benchmark_version_id uuid PRIMARY KEY REFERENCES benchmark.version (benchmark_version_id) ON DELETE CASCADE,
    sent_by text NOT NULL,
    sent_at timestamptz NOT NULL
);
