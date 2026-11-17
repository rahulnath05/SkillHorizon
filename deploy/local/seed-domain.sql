INSERT INTO access.workspace_grant (grant_id, subject_id, workspace, granted_at)
VALUES
    ('11111111-1111-1111-1111-111111111111', 'person-multi', 'employee', now()),
    ('22222222-2222-2222-2222-222222222222', 'person-multi', 'people_manager', now()),
    ('33333333-3333-3333-3333-333333333333', 'person-single', 'employee', now()),
    ('44444444-4444-4444-4444-444444444444', 'person-ld', 'learning_and_development', now()),
    ('55555555-5555-5555-5555-555555555555', 'person-lead', 'competency_lead', now()),
    ('66666666-6666-6666-6666-666666666666', 'person-admin', 'platform_admin', now())
ON CONFLICT (subject_id, workspace) DO NOTHING;
