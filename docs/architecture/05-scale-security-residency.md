# Scale, security, and residency

## Scale envelope

Design to these figures until HR and L&D replace them with measured distributions. They are in [the index](./README.md) and repeated here because the controls hang off them.

| Surface | Design point | Consequence |
|---------|--------------|-------------|
| Identities | 300,000 active, plus closed leavers | Workforce projection is a full copy of the HR population we are allowed to hold, partitioned by residency zone |
| Current skill rows | ~7.5 million | Ordinary for Postgres if person-scoped tables are partitioned and reads go through snapshots |
| History | Several years of sealed results and benchmark versions | History is immutable rows. It is not a rewrite of the current row |
| Job profiles and taxonomy | Thousands of profiles, tens of thousands of skills at most | Cache published reference data. Do not shard it |
| Manager read | Team of roughly 5–20, p95 under a second from snapshots | Indexed reads |
| Competency lead read | 1,000–30,000 people in a practice | Must be a projection. A live scan is a defect |
| Org read | 300,000 | Country and practice rollups only, pre-aggregated |
| Assessment concurrency | 25,000 sittings in one zone | Capacity is configured per zone. Waves above the budget are refused |
| Forbidden peak | 300,000 simultaneous sittings | Activation centre rejects it. Architecture does not size the item service for that accident |
| Evidence | Files, uneven by country and year | Object storage, scanned, retained by policy |
| Audit | Every privileged command | Append-only, retained with the record it describes |
| Workspace entry | One extra read of grants after sign-in | Negligible against the workforce read. The chooser is skipped when the grant set has a single workspace |

HR sync of 300,000 records is a small integration. The hard part is identity match, legal entity, manager changes, dual employment, and contractors. A wrong match attaches scores to the wrong person. That is a data-protection incident, not a cosmetic bug.

## Residency

Personal data is stored in the zone of the person’s legal employing entity, or of the country rule that is stricter when those differ. The zone set is an open question ([07-open-questions.md](./07-open-questions.md)). The structure does not wait on the final list.

**In the zone:** workforce projection for those people, sittings, responses, sealed results, evidence bytes, development paths, manager validations, sessions.

**Global reference, replicated read-only:** published taxonomy versions, published benchmark versions, sealed pack metadata that contains no personal rows. Draft benchmarks stay in the authoring zone until they are published.

**Cross-zone movement that is allowed:** aggregate counts and distributions, an employee id when a manager in another zone must see a readiness figure the policy permits, and the benchmark version id. Raw responses, item text, and evidence bytes do not move to satisfy a dashboard.

A person who changes employing entity is a planned move: close in the old zone under retention, open in the new zone, carry forward the sealed-result summaries the policy allows. The move is a workflow, not an assumption that one database is worldwide.

## Identity and authorization

Authentication is corporate OIDC. The stable key is the HR employee id, mapped at first sign-in and checked on every sync. Email is a contact attribute.

Authorization is relationship plus scope.

| Grant | Source |
|-------|--------|
| Employee workspace | A personal skills or learning assignment exists for this person |
| People manager | HR management relationship, projected |
| Competency lead | Explicit practice scope in SkillHorizon |
| L&D operator | Explicit org or practice scope |
| Platform admin | Explicit, few people, time-bounded where the IdP supports it |
| Break-glass | Separate audited path, expires, cannot be the normal admin role |

Checks happen in the core API on every command and every read of personal data. The session names one active workspace, and a command for a different workspace is refused even when the same person holds that other grant. The web shell hiding a button is not the control. Assessment checks the sitting token and does not trust a person id supplied by the browser alone. After sign-in, one held workspace opens directly. More than one held workspace stops on the entry screen. A workspace that is not held is not offered.

Manager scope changes when HR changes the manager. Yesterday’s validation remains attributed to the manager who made it. Tomorrow’s team page follows the new relationship.

## Assessment integrity

The item bank is the asset that must not leak into logs, exports, or browser caches beyond the sitting.

- Item text is served by the assessment service to the sitting UI.
- Core API logs store item ids only when an incident requires it, and those logs are access-restricted.
- Exposure counters live with assessment so a wave cannot silently burn the whole bank.
- Scoring code runs in assessment. A sealed result is signed by that service and applied by the worker. There is no admin screen that types a proficiency over the top of a sealed result. Appeals produce a new result record that supersedes, with a reason.

## Other threats the design has to absorb

| Threat | Control |
|--------|---------|
| HR feed attaches a score to the wrong person | Match on employee id; quarantine records that collide or lack an id; no email-only merge |
| Manager exports a practice they do not own | Export is a command checked against scope; warehouse feeds are aggregates |
| Admin browses responses | Responses are not in the domain database. Assessment audit logs each read |
| Wave launch overloads a zone | Activation budget is enforced before admission |
| Backup restored into the wrong zone | Backup accounts and keys are per zone. A restore is a change-controlled operation |
| Retention job deletes audit and leaves the score | Retention is by record class. Sealed results and their audit share a schedule |
| A profile menu offers personas the person does not hold, or blends two jobs in one shell | Entry lists only held workspaces, and only when there are two or more. The session is bound to one. Impersonation is a separate break-glass path, ticketed, written to audit, and disabled for assessment item delivery |

## Availability and recovery

| Dependency | Target posture |
|------------|----------------|
| Core API and domain Postgres | Zone-local failover. Authoritative for that zone’s personal data |
| Assessment | Same zone. Sittings resume after failover from Postgres, not from a browser draft |
| Reference replica | Stale reads of published benchmarks are acceptable for a short replication lag. Publication is rare and human |
| Notifications | Degrade first. Mail lag does not stop sittings, seals, or validation |
| HR sync | Lag is visible. Interactive reads use the last good projection |

Recovery point for domain and assessment databases is on the order of minutes, not a nightly dump alone. A lost sitting that had not been flushed is the failure mode Redis must not be allowed to create: response commits land in assessment Postgres before the next item is served.

## Performance budgets

These are acceptance targets for the first production zone, to be turned into tests when the API exists.

| Interaction | Budget |
|-------------|--------|
| Team readiness page | p95 under 1 second, excluding SSO |
| Person path page | p95 under 1 second |
| Practice rollup page | p95 under 2 seconds from a snapshot no older than the published lag |
| Start sitting | p95 under 2 seconds to first item, given an admitted wave |
| Publish benchmark | Synchronous commit of an immutable version. Fan-out to zones is asynchronous and visible as replication lag |
| HR sync | A full reconciliation completes overnight. Incremental changes apply within an hour of the feed |

A page that meets the budget by caching personal data in the browser across users is a defect.
