CREATE SCHEMA IF NOT EXISTS messaging;

CREATE TABLE messaging.outbox (
    outbox_id uuid PRIMARY KEY,
    zone_id text NOT NULL,
    message_type text NOT NULL,
    payload jsonb NOT NULL,
    correlation_id text NOT NULL,
    occurred_at timestamptz NOT NULL,
    published_at timestamptz NULL
);

CREATE INDEX outbox_unpublished_idx
    ON messaging.outbox (occurred_at)
    WHERE published_at IS NULL;
