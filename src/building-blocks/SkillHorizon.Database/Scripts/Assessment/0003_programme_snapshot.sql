CREATE TABLE assessment.programme_snapshot (
    snapshot_id uuid PRIMARY KEY,
    kind text NOT NULL,
    wave_id uuid,
    as_of timestamptz NOT NULL,
    completed_count integer NOT NULL,
    open_count integer NOT NULL,
    duration_seconds integer NOT NULL,
    CONSTRAINT programme_kind_known CHECK (kind IN ('operations', 'scorecard', 'pilot')),
    CONSTRAINT programme_pilot_wave CHECK ((kind = 'pilot') = (wave_id IS NOT NULL)),
    CONSTRAINT programme_counts_known CHECK (completed_count >= 0 AND open_count >= 0 AND duration_seconds >= 0)
);
