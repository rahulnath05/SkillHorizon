# 06 Territories

Parent: [Master plan](README.md)

Status: `done`

Depends on [05 Practice](05-practice.md) being `done`.

## Outcome

The first three or four zones run from the same template as the first zone. Published reference data is replicated. Responses do not move. A cross-zone view shows aggregates only. A person whose legal entity is not mapped cannot be written into an existing zone. A later territory repeats the same register-and-map procedure with no country-specific build.

## Out of scope

A shared database across countries, a fork of the product per territory, and Mira. Warehouse export stays in slice 07.

## Micro-steps

| Step | Work | Status |
|------|------|--------|
| 06.1 | Infrastructure template that provisions web, core API, learning, worker, three PostgreSQL databases, Redis, object storage, and the queue | done |
| 06.2 | Bring up the remaining zones of the first wave with that template | done |
| 06.3 | Register each zone and map its legal entities. An unmapped entity is refused at sign-in | done |
| 06.4 | Subscribe each zone to published taxonomy and benchmark replication. Drafts stay in the authoring zone | done |
| 06.5 | Turn on the HR feed for the mapped entities only | done |
| 06.6 | Prove a cross-zone figure is an aggregate, and that raw responses were not copied | done |
| 06.7 | Document the repeat procedure for the next territory: provision, register, map, subscribe, enable the feed, then enable capabilities | done |
