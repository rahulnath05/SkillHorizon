ALTER TABLE assessment.item DROP CONSTRAINT item_skill_unique;

ALTER TABLE assessment.item
    ADD COLUMN difficulty integer NOT NULL DEFAULT 1;

ALTER TABLE assessment.item
    ADD CONSTRAINT item_difficulty_known CHECK (difficulty BETWEEN 1 AND 5);

CREATE TABLE assessment.rubric (
    item_id uuid PRIMARY KEY REFERENCES assessment.item (item_id),
    body text NOT NULL,
    CONSTRAINT rubric_body_present CHECK (length(btrim(body)) > 0)
);

CREATE TABLE assessment.mark (
    sitting_id uuid NOT NULL REFERENCES assessment.sitting (sitting_id),
    item_id uuid NOT NULL REFERENCES assessment.item (item_id),
    response_body text NOT NULL,
    band integer NOT NULL,
    marked_at timestamptz NOT NULL,
    PRIMARY KEY (sitting_id, item_id),
    CONSTRAINT mark_band_known CHECK (band BETWEEN 1 AND 5),
    CONSTRAINT mark_body_present CHECK (length(btrim(response_body)) > 0)
);

CREATE TABLE assessment.sme (
    sme_id uuid PRIMARY KEY,
    domain_name text NOT NULL,
    expert_name text NOT NULL,
    credentials text NOT NULL,
    CONSTRAINT sme_domain_unique UNIQUE (domain_name),
    CONSTRAINT sme_name_present CHECK (length(btrim(expert_name)) > 0),
    CONSTRAINT sme_credentials_present CHECK (length(btrim(credentials)) > 0)
);
