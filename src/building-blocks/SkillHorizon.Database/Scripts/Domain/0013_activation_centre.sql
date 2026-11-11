ALTER TABLE workforce.person
    ADD COLUMN joined_at timestamptz;

UPDATE workforce.person
SET joined_at = projected_at
WHERE joined_at IS NULL;

ALTER TABLE activation.admission
    ADD COLUMN admitted_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN route text NOT NULL DEFAULT 'launch';

ALTER TABLE activation.admission
    ADD CONSTRAINT admission_route_known CHECK (route IN ('launch', 'rolling'));

ALTER TABLE activation.admission
    ALTER COLUMN admitted_at DROP DEFAULT,
    ALTER COLUMN route DROP DEFAULT;
