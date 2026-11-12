CREATE TABLE audit.model_register (
    model_id uuid PRIMARY KEY,
    model_name text NOT NULL,
    purpose text NOT NULL,
    connected boolean NOT NULL,
    registered_at timestamptz NOT NULL,
    CONSTRAINT model_name_present CHECK (length(btrim(model_name)) > 0),
    CONSTRAINT model_purpose_present CHECK (length(btrim(purpose)) > 0)
);

CREATE TABLE audit.malpractice_case (
    case_id uuid PRIMARY KEY,
    sitting_id uuid NOT NULL,
    status text NOT NULL,
    opened_by text NOT NULL,
    opened_at timestamptz NOT NULL,
    CONSTRAINT malpractice_status_known CHECK (status IN ('open', 'explained', 'set_aside')),
    CONSTRAINT malpractice_opened_by_present CHECK (length(btrim(opened_by)) > 0)
);

CREATE TABLE audit.personal_export (
    export_id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    requested_by text NOT NULL,
    requested_at timestamptz NOT NULL,
    CONSTRAINT personal_export_subject_present CHECK (length(btrim(subject_id)) > 0)
);
