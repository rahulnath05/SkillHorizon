# Adding a territory

A territory is another residency zone of the same product. It is not a fork and it does not share a database with another zone. The control registry is the one global database, and it stores no people, responses, or evidence.

The first wave is three zones:

| Zone | Legal entity | Role |
|------|----------------|------|
| `local` | `LE-LOCAL` | Authoring zone. Already provisioned by the installation compose file |
| `north` | `LE-NORTH` | Residency zone |
| `south` | `LE-SOUTH` | Residency zone |

`LE-OTHER` is not mapped. Sign-in for that entity is refused, and the HR feed does not write it into a zone that already exists.

Repeat this procedure for the next territory. Nothing in the list is specific to a country.

1. Provision the zone from `deploy/zone/compose.yaml`. That starts web, the core API, learning, and the worker, plus that zone's three PostgreSQL databases (domain, assessment, progress), Redis, an object-storage volume, and the zone's queue. Set `ZONE_ID` and the host ports so they do not collide with a zone that is already running. On a machine that already has PostgreSQL, `deploy/territories/provision-zone.sh` creates the three databases without reusing another zone's database name.
2. Register the zone in the control registry with status `provisioning`, its API address, and its queue address.
3. Map the legal entities that belong in that zone. An entity with no mapping is refused at sign-in. It is not written into some other zone.
4. Subscribe the zone to published taxonomy and benchmark replication. Drafts stay in the authoring zone. Only a published message is delivered.
5. Turn on the HR feed. The feed projects a person only when their legal entity maps to this zone and the zone is active.
6. Mark the zone `active`. Activation is refused until the reference subscription and the HR feed are on.

The authoring zone fans published reference messages onto each subscribed zone's queue. A zone that is not subscribed receives nothing. Raw responses and evidence bytes stay in the zone that wrote them. A cross-zone practice figure is the sum of each zone's stored counts, read from `GET /aggregates/practice` on the control registry.
