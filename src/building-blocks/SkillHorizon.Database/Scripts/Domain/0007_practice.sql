CREATE TABLE access.practice_scope (
    subject_id text NOT NULL,
    practice_code text NOT NULL,
    granted_at timestamptz NOT NULL,
    PRIMARY KEY (subject_id, practice_code)
);

CREATE TABLE attestation.operating_limit (
    zone_id text PRIMARY KEY,
    maximum integer NOT NULL,
    CONSTRAINT operating_limit_positive CHECK (maximum > 0)
);

CREATE TABLE attestation.pack (
    pack_id uuid PRIMARY KEY,
    practice_code text NOT NULL,
    status text NOT NULL,
    submitted_by text NOT NULL,
    submitted_at timestamptz NOT NULL,
    CONSTRAINT pack_status_known CHECK (status IN ('submitted', 'sealed'))
);

CREATE TABLE attestation.pack_row (
    row_id uuid PRIMARY KEY,
    pack_id uuid NOT NULL REFERENCES attestation.pack (pack_id),
    job_profile_id uuid NOT NULL,
    profile_code text NOT NULL,
    profile_name text NOT NULL,
    classification text NOT NULL,
    exception_note text,
    CONSTRAINT pack_row_classification_known CHECK (classification IN ('aligned', 'tightening', 'new_bar')),
    CONSTRAINT pack_row_exception CHECK (
        (classification = 'aligned' AND exception_note IS NULL)
        OR (classification IN ('tightening', 'new_bar') AND exception_note IS NOT NULL AND length(btrim(exception_note)) > 0)
    ),
    CONSTRAINT pack_row_profile_unique UNIQUE (pack_id, job_profile_id)
);

CREATE TABLE attestation.seal (
    seal_id uuid PRIMARY KEY,
    pack_id uuid NOT NULL UNIQUE REFERENCES attestation.pack (pack_id),
    taxonomy_version_id uuid NOT NULL,
    lead_subject_id text NOT NULL,
    sealed_at timestamptz NOT NULL
);

CREATE TABLE attestation.sealed_benchmark (
    seal_id uuid NOT NULL REFERENCES attestation.seal (seal_id),
    job_profile_id uuid NOT NULL,
    benchmark_version_id uuid NOT NULL,
    PRIMARY KEY (seal_id, job_profile_id)
);

CREATE TABLE attestation.baseline (
    practice_code text PRIMARY KEY,
    pack_id uuid NOT NULL REFERENCES attestation.pack (pack_id),
    taxonomy_version_id uuid NOT NULL,
    sealed_at timestamptz NOT NULL
);

CREATE TABLE attestation.cohort (
    cohort_id uuid PRIMARY KEY,
    practice_code text NOT NULL,
    country_code text,
    org_unit text,
    job_profile_id uuid NOT NULL,
    lead_subject_id text NOT NULL,
    created_at timestamptz NOT NULL,
    CONSTRAINT cohort_scope_present CHECK (
        (country_code IS NOT NULL AND length(btrim(country_code)) > 0)
        OR (org_unit IS NOT NULL AND length(btrim(org_unit)) > 0)
    )
);

CREATE TABLE attestation.cohort_member (
    cohort_id uuid NOT NULL REFERENCES attestation.cohort (cohort_id),
    employee_id text NOT NULL,
    PRIMARY KEY (cohort_id, employee_id)
);

CREATE TABLE readiness.snapshot (
    snapshot_id uuid PRIMARY KEY,
    subject_kind text NOT NULL,
    subject_key text NOT NULL,
    as_of timestamptz NOT NULL,
    dirty boolean NOT NULL,
    member_count integer NOT NULL,
    measured_count integer NOT NULL,
    gap_count integer NOT NULL,
    cohort_count integer NOT NULL,
    CONSTRAINT snapshot_kind_known CHECK (subject_kind = 'practice'),
    CONSTRAINT snapshot_subject_unique UNIQUE (subject_kind, subject_key),
    CONSTRAINT snapshot_counts_non_negative CHECK (
        member_count >= 0 AND measured_count >= 0 AND gap_count >= 0 AND cohort_count >= 0
    )
);
