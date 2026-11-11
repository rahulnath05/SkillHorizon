CREATE TABLE workforce.team_snapshot (
    manager_employee_id text PRIMARY KEY,
    as_of timestamptz NOT NULL,
    report_count integer NOT NULL,
    attention_count integer NOT NULL,
    CONSTRAINT team_snapshot_counts_known CHECK (report_count >= 0 AND attention_count >= 0 AND attention_count <= report_count)
);

CREATE TABLE workforce.team_member (
    manager_employee_id text NOT NULL,
    employee_id text NOT NULL,
    display_name text NOT NULL,
    position text NOT NULL,
    attention boolean NOT NULL,
    PRIMARY KEY (manager_employee_id, employee_id),
    CONSTRAINT team_member_position_known CHECK (position IN ('met', 'gap', 'not-measured'))
);

CREATE TABLE workforce.profile_confirmation (
    manager_employee_id text NOT NULL,
    report_employee_id text NOT NULL,
    job_profile_id uuid NOT NULL,
    recorded_at timestamptz NOT NULL,
    PRIMARY KEY (manager_employee_id, report_employee_id, job_profile_id)
);

CREATE TABLE workforce.assessment_review (
    manager_employee_id text NOT NULL,
    report_employee_id text NOT NULL,
    result_id uuid NOT NULL,
    note text NOT NULL,
    recorded_at timestamptz NOT NULL,
    PRIMARY KEY (manager_employee_id, result_id),
    CONSTRAINT assessment_review_note_length CHECK (char_length(note) <= 500)
);

CREATE TABLE workforce.profile_correction (
    request_id uuid PRIMARY KEY,
    manager_employee_id text NOT NULL,
    report_employee_id text NOT NULL,
    note text NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT profile_correction_note_length CHECK (char_length(note) BETWEEN 1 AND 500)
);

CREATE TABLE workforce.coaching_commitment (
    commitment_id uuid PRIMARY KEY,
    manager_employee_id text NOT NULL,
    report_employee_id text NOT NULL,
    title text NOT NULL,
    due_on date NOT NULL,
    recorded_at timestamptz NOT NULL,
    CONSTRAINT coaching_title_length CHECK (char_length(title) BETWEEN 1 AND 200)
);
