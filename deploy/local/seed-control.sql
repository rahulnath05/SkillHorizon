INSERT INTO zone.zone (zone_id, display_name, status, api_base_url, registered_at)
VALUES ('local', 'Local zone', 'active', 'http://api:8080', now())
ON CONFLICT (zone_id) DO NOTHING;

INSERT INTO routing.legal_entity (legal_entity_id, zone_id, display_name)
VALUES ('LE-LOCAL', 'local', 'Local employing entity')
ON CONFLICT (legal_entity_id) DO NOTHING;
