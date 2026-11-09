CREATE TABLE access.workspace_grant (
    grant_id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    workspace text NOT NULL,
    granted_at timestamptz NOT NULL,
    CONSTRAINT workspace_grant_workspace_known CHECK (
        workspace IN (
            'employee',
            'people_manager',
            'competency_lead',
            'learning_and_development',
            'platform_admin'
        )
    ),
    CONSTRAINT workspace_grant_subject_workspace UNIQUE (subject_id, workspace)
);
