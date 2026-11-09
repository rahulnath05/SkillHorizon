CREATE SCHEMA IF NOT EXISTS zone;
CREATE SCHEMA IF NOT EXISTS routing;
CREATE SCHEMA IF NOT EXISTS integration;
CREATE SCHEMA IF NOT EXISTS capability;

CREATE TABLE zone.zone (
    zone_id text PRIMARY KEY,
    display_name text NOT NULL,
    status text NOT NULL,
    api_base_url text NOT NULL,
    registered_at timestamptz NOT NULL,
    CONSTRAINT zone_status_known CHECK (status IN ('provisioning', 'active'))
);

CREATE TABLE routing.legal_entity (
    legal_entity_id text PRIMARY KEY,
    zone_id text NOT NULL REFERENCES zone.zone (zone_id),
    display_name text NOT NULL
);

CREATE TABLE integration.connector (
    connector_id text PRIMARY KEY,
    zone_id text NOT NULL REFERENCES zone.zone (zone_id),
    connector_type text NOT NULL,
    endpoint text NULL,
    secret_name text NULL,
    enabled boolean NOT NULL
);

CREATE TABLE capability.flag (
    zone_id text NOT NULL REFERENCES zone.zone (zone_id),
    capability_code text NOT NULL,
    enabled boolean NOT NULL,
    PRIMARY KEY (zone_id, capability_code)
);
