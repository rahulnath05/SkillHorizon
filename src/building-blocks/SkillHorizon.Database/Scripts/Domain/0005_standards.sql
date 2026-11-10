CREATE TABLE job_architecture.framework (
    framework_id uuid PRIMARY KEY,
    code text NOT NULL,
    display_name text NOT NULL,
    CONSTRAINT framework_code_unique UNIQUE (code)
);

CREATE TABLE job_architecture.level (
    level_id uuid PRIMARY KEY,
    framework_id uuid NOT NULL REFERENCES job_architecture.framework (framework_id),
    code text NOT NULL,
    display_name text NOT NULL,
    rank integer NOT NULL,
    CONSTRAINT level_code_unique UNIQUE (framework_id, code),
    CONSTRAINT level_rank_unique UNIQUE (framework_id, rank),
    CONSTRAINT level_rank_positive CHECK (rank > 0)
);

CREATE TABLE job_architecture.job_profile (
    profile_id uuid PRIMARY KEY,
    framework_id uuid NOT NULL REFERENCES job_architecture.framework (framework_id),
    level_id uuid NOT NULL REFERENCES job_architecture.level (level_id),
    code text NOT NULL,
    display_name text NOT NULL,
    CONSTRAINT job_profile_code_unique UNIQUE (code)
);

CREATE TABLE taxonomy.practice (
    practice_id uuid PRIMARY KEY,
    code text NOT NULL,
    display_name text NOT NULL,
    CONSTRAINT practice_code_unique UNIQUE (code)
);

CREATE TABLE taxonomy.competency (
    competency_id uuid PRIMARY KEY,
    practice_id uuid NOT NULL REFERENCES taxonomy.practice (practice_id),
    code text NOT NULL,
    display_name text NOT NULL,
    CONSTRAINT competency_code_unique UNIQUE (code)
);

CREATE TABLE taxonomy.skill (
    skill_id uuid PRIMARY KEY,
    competency_id uuid NOT NULL REFERENCES taxonomy.competency (competency_id),
    code text NOT NULL,
    display_name text NOT NULL,
    CONSTRAINT skill_code_unique UNIQUE (code)
);

CREATE TABLE taxonomy.version (
    taxonomy_version_id uuid PRIMARY KEY,
    version_number integer NOT NULL,
    status text NOT NULL,
    published_at timestamptz,
    superseded_at timestamptz,
    superseded_by uuid,
    created_at timestamptz NOT NULL,
    CONSTRAINT taxonomy_version_number_unique UNIQUE (version_number),
    CONSTRAINT taxonomy_version_status_known CHECK (status IN ('draft', 'published', 'superseded')),
    CONSTRAINT taxonomy_version_published_stamp CHECK (
        (status = 'draft' AND published_at IS NULL)
        OR (status IN ('published', 'superseded') AND published_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX taxonomy_one_draft ON taxonomy.version ((1)) WHERE status = 'draft';
CREATE UNIQUE INDEX taxonomy_one_published ON taxonomy.version ((1)) WHERE status = 'published';

CREATE TABLE taxonomy.version_skill (
    taxonomy_version_id uuid NOT NULL REFERENCES taxonomy.version (taxonomy_version_id),
    skill_id uuid NOT NULL,
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    competency_code text NOT NULL,
    competency_name text NOT NULL,
    practice_code text NOT NULL,
    practice_name text NOT NULL,
    PRIMARY KEY (taxonomy_version_id, skill_id)
);

CREATE TABLE taxonomy.proficiency (
    taxonomy_version_id uuid NOT NULL REFERENCES taxonomy.version (taxonomy_version_id),
    code text NOT NULL,
    display_name text NOT NULL,
    rank integer NOT NULL,
    PRIMARY KEY (taxonomy_version_id, rank),
    CONSTRAINT proficiency_code_unique UNIQUE (taxonomy_version_id, code),
    CONSTRAINT proficiency_rank_positive CHECK (rank > 0)
);

CREATE TABLE benchmark.version (
    benchmark_version_id uuid PRIMARY KEY,
    job_profile_id uuid NOT NULL REFERENCES job_architecture.job_profile (profile_id),
    taxonomy_version_id uuid NOT NULL REFERENCES taxonomy.version (taxonomy_version_id),
    version_number integer NOT NULL,
    status text NOT NULL,
    supersedes_version_id uuid,
    published_at timestamptz,
    superseded_at timestamptz,
    superseded_by uuid,
    created_at timestamptz NOT NULL,
    CONSTRAINT benchmark_version_number_unique UNIQUE (job_profile_id, version_number),
    CONSTRAINT benchmark_version_status_known CHECK (status IN ('draft', 'published', 'superseded')),
    CONSTRAINT benchmark_version_published_stamp CHECK (
        (status = 'draft' AND published_at IS NULL)
        OR (status IN ('published', 'superseded') AND published_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX benchmark_one_draft ON benchmark.version (job_profile_id) WHERE status = 'draft';
CREATE UNIQUE INDEX benchmark_one_published ON benchmark.version (job_profile_id) WHERE status = 'published';

CREATE TABLE benchmark.target (
    benchmark_version_id uuid NOT NULL REFERENCES benchmark.version (benchmark_version_id),
    skill_id uuid NOT NULL,
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    competency_name text NOT NULL,
    practice_name text NOT NULL,
    proficiency_rank integer NOT NULL,
    proficiency_code text NOT NULL,
    proficiency_name text NOT NULL,
    PRIMARY KEY (benchmark_version_id, skill_id)
);

CREATE TABLE audit.audit_record (
    audit_id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    action text NOT NULL,
    record_type text NOT NULL,
    record_id uuid NOT NULL,
    correlation_id text NOT NULL,
    detail jsonb NOT NULL,
    occurred_at timestamptz NOT NULL
);

CREATE INDEX audit_record_idx ON audit.audit_record (record_type, record_id, occurred_at);

CREATE SCHEMA IF NOT EXISTS reference;

CREATE TABLE reference.applied_message (
    outbox_id uuid PRIMARY KEY,
    applied_at timestamptz NOT NULL
);

CREATE TABLE reference.taxonomy_version (
    taxonomy_version_id uuid PRIMARY KEY,
    version_number integer NOT NULL,
    status text NOT NULL,
    published_at timestamptz NOT NULL,
    superseded_at timestamptz,
    superseded_by uuid,
    CONSTRAINT reference_taxonomy_status_known CHECK (status IN ('published', 'superseded'))
);

CREATE TABLE reference.taxonomy_skill (
    taxonomy_version_id uuid NOT NULL REFERENCES reference.taxonomy_version (taxonomy_version_id),
    skill_id uuid NOT NULL,
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    competency_code text NOT NULL,
    competency_name text NOT NULL,
    practice_code text NOT NULL,
    practice_name text NOT NULL,
    PRIMARY KEY (taxonomy_version_id, skill_id)
);

CREATE TABLE reference.proficiency (
    taxonomy_version_id uuid NOT NULL REFERENCES reference.taxonomy_version (taxonomy_version_id),
    code text NOT NULL,
    display_name text NOT NULL,
    rank integer NOT NULL,
    PRIMARY KEY (taxonomy_version_id, rank)
);

CREATE TABLE reference.benchmark_version (
    benchmark_version_id uuid PRIMARY KEY,
    job_profile_id uuid NOT NULL,
    profile_code text NOT NULL,
    profile_name text NOT NULL,
    framework_code text NOT NULL,
    framework_name text NOT NULL,
    level_code text NOT NULL,
    level_name text NOT NULL,
    level_rank integer NOT NULL,
    taxonomy_version_id uuid NOT NULL,
    version_number integer NOT NULL,
    status text NOT NULL,
    supersedes_version_id uuid,
    published_at timestamptz NOT NULL,
    superseded_at timestamptz,
    superseded_by uuid,
    CONSTRAINT reference_benchmark_status_known CHECK (status IN ('published', 'superseded'))
);

CREATE TABLE reference.benchmark_target (
    benchmark_version_id uuid NOT NULL REFERENCES reference.benchmark_version (benchmark_version_id),
    skill_id uuid NOT NULL,
    skill_code text NOT NULL,
    skill_name text NOT NULL,
    competency_name text NOT NULL,
    practice_name text NOT NULL,
    proficiency_rank integer NOT NULL,
    proficiency_code text NOT NULL,
    proficiency_name text NOT NULL,
    PRIMARY KEY (benchmark_version_id, skill_id)
);

CREATE FUNCTION taxonomy.guard_version() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'draft' THEN
            RAISE EXCEPTION 'published taxonomy versions are immutable';
        END IF;
        RETURN OLD;
    END IF;

    IF OLD.status = 'superseded' THEN
        RAISE EXCEPTION 'superseded taxonomy versions are immutable';
    END IF;

    IF OLD.status = 'published' THEN
        IF NEW.status <> 'superseded'
            OR NEW.version_number <> OLD.version_number
            OR NEW.published_at IS DISTINCT FROM OLD.published_at
            OR NEW.taxonomy_version_id <> OLD.taxonomy_version_id
            OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
            RAISE EXCEPTION 'published taxonomy versions are immutable';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER taxonomy_version_guard
    BEFORE UPDATE OR DELETE ON taxonomy.version
    FOR EACH ROW EXECUTE FUNCTION taxonomy.guard_version();

CREATE FUNCTION taxonomy.guard_snapshot() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    parent_status text;
    parent_id uuid;
BEGIN
    parent_id := COALESCE(OLD.taxonomy_version_id, NEW.taxonomy_version_id);
    SELECT status INTO parent_status FROM taxonomy.version WHERE taxonomy_version_id = parent_id;
    IF parent_status IS DISTINCT FROM 'draft' THEN
        RAISE EXCEPTION 'published taxonomy snapshots are immutable';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER taxonomy_skill_guard
    BEFORE UPDATE OR DELETE ON taxonomy.version_skill
    FOR EACH ROW EXECUTE FUNCTION taxonomy.guard_snapshot();

CREATE TRIGGER taxonomy_proficiency_guard
    BEFORE UPDATE OR DELETE ON taxonomy.proficiency
    FOR EACH ROW EXECUTE FUNCTION taxonomy.guard_snapshot();

CREATE FUNCTION benchmark.guard_version() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'draft' THEN
            RAISE EXCEPTION 'published benchmark versions are immutable';
        END IF;
        RETURN OLD;
    END IF;

    IF OLD.status = 'superseded' THEN
        RAISE EXCEPTION 'superseded benchmark versions are immutable';
    END IF;

    IF OLD.status = 'published' THEN
        IF NEW.status <> 'superseded'
            OR NEW.job_profile_id <> OLD.job_profile_id
            OR NEW.taxonomy_version_id <> OLD.taxonomy_version_id
            OR NEW.version_number <> OLD.version_number
            OR NEW.supersedes_version_id IS DISTINCT FROM OLD.supersedes_version_id
            OR NEW.published_at IS DISTINCT FROM OLD.published_at
            OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
            RAISE EXCEPTION 'published benchmark versions are immutable';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER benchmark_version_guard
    BEFORE UPDATE OR DELETE ON benchmark.version
    FOR EACH ROW EXECUTE FUNCTION benchmark.guard_version();

CREATE FUNCTION benchmark.guard_target() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    parent_status text;
    parent_id uuid;
BEGIN
    parent_id := COALESCE(OLD.benchmark_version_id, NEW.benchmark_version_id);
    SELECT status INTO parent_status FROM benchmark.version WHERE benchmark_version_id = parent_id;
    IF parent_status IS DISTINCT FROM 'draft' THEN
        RAISE EXCEPTION 'published benchmark targets are immutable';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER benchmark_target_guard
    BEFORE UPDATE OR DELETE ON benchmark.target
    FOR EACH ROW EXECUTE FUNCTION benchmark.guard_target();

CREATE FUNCTION reference.guard_taxonomy_version() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' OR OLD.status <> 'published' OR NEW.status <> 'superseded'
        OR NEW.taxonomy_version_id <> OLD.taxonomy_version_id
        OR NEW.version_number <> OLD.version_number
        OR NEW.published_at IS DISTINCT FROM OLD.published_at THEN
        RAISE EXCEPTION 'reference publications are immutable';
    END IF;
    RETURN NEW;
END;
$$;

CREATE FUNCTION reference.guard_benchmark_version() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' OR OLD.status <> 'published' OR NEW.status <> 'superseded'
        OR NEW.benchmark_version_id <> OLD.benchmark_version_id
        OR NEW.job_profile_id <> OLD.job_profile_id
        OR NEW.profile_code <> OLD.profile_code
        OR NEW.profile_name <> OLD.profile_name
        OR NEW.framework_code <> OLD.framework_code
        OR NEW.framework_name <> OLD.framework_name
        OR NEW.level_code <> OLD.level_code
        OR NEW.level_name <> OLD.level_name
        OR NEW.level_rank <> OLD.level_rank
        OR NEW.taxonomy_version_id <> OLD.taxonomy_version_id
        OR NEW.version_number <> OLD.version_number
        OR NEW.supersedes_version_id IS DISTINCT FROM OLD.supersedes_version_id
        OR NEW.published_at IS DISTINCT FROM OLD.published_at THEN
        RAISE EXCEPTION 'reference publications are immutable';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER reference_taxonomy_guard
    BEFORE UPDATE OR DELETE ON reference.taxonomy_version
    FOR EACH ROW EXECUTE FUNCTION reference.guard_taxonomy_version();

CREATE TRIGGER reference_benchmark_guard
    BEFORE UPDATE OR DELETE ON reference.benchmark_version
    FOR EACH ROW EXECUTE FUNCTION reference.guard_benchmark_version();

CREATE FUNCTION reference.guard_child() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'reference publications are immutable';
END;
$$;

CREATE TRIGGER reference_taxonomy_skill_guard
    BEFORE UPDATE OR DELETE ON reference.taxonomy_skill
    FOR EACH ROW EXECUTE FUNCTION reference.guard_child();

CREATE TRIGGER reference_proficiency_guard
    BEFORE UPDATE OR DELETE ON reference.proficiency
    FOR EACH ROW EXECUTE FUNCTION reference.guard_child();

CREATE TRIGGER reference_benchmark_target_guard
    BEFORE UPDATE OR DELETE ON reference.benchmark_target
    FOR EACH ROW EXECUTE FUNCTION reference.guard_child();
