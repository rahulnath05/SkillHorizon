CREATE TABLE workforce.person (
    employee_id text PRIMARY KEY,
    legal_entity_id text NOT NULL,
    country_code text NOT NULL,
    manager_employee_id text,
    employment_status text NOT NULL,
    personal_learning_assigned boolean NOT NULL,
    display_name text NOT NULL,
    closed_at timestamptz,
    projected_at timestamptz NOT NULL,
    CONSTRAINT person_status_known CHECK (employment_status IN ('active', 'closed')),
    CONSTRAINT person_closed_with_timestamp CHECK (
        (employment_status = 'closed' AND closed_at IS NOT NULL)
        OR (employment_status = 'active' AND closed_at IS NULL)
    )
);

CREATE INDEX person_manager_idx ON workforce.person (manager_employee_id);

CREATE TABLE workforce.quarantine (
    quarantine_id uuid PRIMARY KEY,
    employee_id text,
    reason text NOT NULL,
    detail text NOT NULL,
    recorded_at timestamptz NOT NULL
);

CREATE TABLE workforce.management_relationship (
    manager_employee_id text NOT NULL,
    report_employee_id text NOT NULL,
    PRIMARY KEY (manager_employee_id, report_employee_id)
);

CREATE INDEX management_relationship_manager_idx
    ON workforce.management_relationship (manager_employee_id);
