ALTER TABLE taxonomy.skill
    ADD COLUMN category text NOT NULL DEFAULT '',
    ADD COLUMN definition text NOT NULL DEFAULT '',
    ADD COLUMN status text NOT NULL DEFAULT 'active';

ALTER TABLE taxonomy.skill
    ADD CONSTRAINT skill_status_known CHECK (status IN ('draft', 'active'));

ALTER TABLE taxonomy.version_skill
    ADD COLUMN category text NOT NULL DEFAULT '',
    ADD COLUMN definition text NOT NULL DEFAULT '',
    ADD COLUMN status text NOT NULL DEFAULT 'active';

ALTER TABLE taxonomy.version_skill
    ADD CONSTRAINT version_skill_status_known CHECK (status IN ('draft', 'active'));

ALTER TABLE taxonomy.competency
    ADD COLUMN com_lead text NOT NULL DEFAULT '',
    ADD COLUMN approval_status text NOT NULL DEFAULT 'draft';

ALTER TABLE taxonomy.competency
    ADD CONSTRAINT competency_approval_known CHECK (approval_status IN ('draft', 'review', 'changes-requested', 'approved'));

ALTER TABLE taxonomy.proficiency
    ADD COLUMN meaning text NOT NULL DEFAULT '';

ALTER TABLE job_architecture.job_profile
    ADD COLUMN purpose text NOT NULL DEFAULT '',
    ADD COLUMN band text NOT NULL DEFAULT '',
    ADD COLUMN family_name text NOT NULL DEFAULT '',
    ADD COLUMN competency_code text;

ALTER TABLE benchmark.target
    ADD COLUMN suggested_rank integer,
    ADD COLUMN level_set boolean NOT NULL DEFAULT true;

CREATE TABLE taxonomy.overlap_note (
    note_id uuid PRIMARY KEY,
    title text NOT NULL,
    detail text NOT NULL,
    CONSTRAINT overlap_title_present CHECK (length(btrim(title)) > 0),
    CONSTRAINT overlap_detail_present CHECK (length(btrim(detail)) > 0)
);

ALTER TABLE workforce.profile_correction
    ADD COLUMN title text NOT NULL DEFAULT '',
    ADD COLUMN evidence text NOT NULL DEFAULT '',
    ADD COLUMN request_status text NOT NULL DEFAULT 'new',
    ADD COLUMN competency_code text;

ALTER TABLE workforce.profile_correction
    ADD CONSTRAINT profile_request_status_known CHECK (request_status IN ('new', 'needs-context', 'waiting', 'in-progress'));

CREATE TABLE readiness.skill_alias (
    alias_id uuid PRIMARY KEY,
    skill_code text NOT NULL,
    alias_name text NOT NULL,
    CONSTRAINT skill_alias_unique UNIQUE (skill_code, alias_name),
    CONSTRAINT skill_alias_name_present CHECK (length(btrim(alias_name)) > 0)
);

CREATE TABLE activation.cycle (
    zone_id text PRIMARY KEY,
    cycle_name text NOT NULL,
    due_on date,
    self_assessment boolean NOT NULL,
    manager_validation boolean NOT NULL,
    work_evidence boolean NOT NULL,
    CONSTRAINT cycle_name_present CHECK (length(btrim(cycle_name)) > 0)
);
