CREATE TABLE messaging.reference_delivery (
    outbox_id uuid NOT NULL REFERENCES messaging.outbox (outbox_id),
    subscriber_zone_id text NOT NULL,
    delivered_at timestamptz NOT NULL,
    PRIMARY KEY (outbox_id, subscriber_zone_id)
);
