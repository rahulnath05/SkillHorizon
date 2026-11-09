INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
VALUES
    ('11111111-1111-1111-1111-111111111111', 'person-multi', 'employee', now()),
    ('22222222-2222-2222-2222-222222222222', 'person-multi', 'people_manager', now()),
    ('33333333-3333-3333-3333-333333333333', 'person-single', 'employee', now())
ON CONFLICT (subject_id, workspace) DO NOTHING;
