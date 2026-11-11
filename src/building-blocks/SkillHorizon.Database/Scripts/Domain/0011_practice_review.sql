CREATE TABLE attestation.row_review (
    row_id uuid PRIMARY KEY REFERENCES attestation.pack_row (row_id),
    reviewed_by text NOT NULL,
    reviewed_at timestamptz NOT NULL
);

CREATE TABLE attestation.exception_decision (
    row_id uuid PRIMARY KEY REFERENCES attestation.pack_row (row_id),
    decision text NOT NULL,
    decided_by text NOT NULL,
    decided_at timestamptz NOT NULL,
    CONSTRAINT exception_decision_length CHECK (char_length(btrim(decision)) BETWEEN 1 AND 500)
);

CREATE TABLE attestation.request_acceptance (
    request_id uuid PRIMARY KEY REFERENCES workforce.profile_correction (request_id),
    practice_code text NOT NULL,
    accepted_by text NOT NULL,
    accepted_at timestamptz NOT NULL
);
