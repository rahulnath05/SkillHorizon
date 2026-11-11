ALTER TABLE progress.step
    ADD COLUMN due_on date;

UPDATE progress.step
SET due_on = (recorded_at AT TIME ZONE 'UTC')::date + 28
WHERE provider = 'external'
  AND path_id IS NOT NULL
  AND due_on IS NULL;
