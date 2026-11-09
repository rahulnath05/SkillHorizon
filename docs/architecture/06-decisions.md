# Decisions

Status of ADR-004 is **Deferred** in time, not dropped. ADR-011, ADR-012, ADR-013, ADR-014, and ADR-015 are **Accepted** as of 9 November 2026. Every other ADR below is **Proposed**. Accepting one is a human decision. Implementation should follow accepted ADRs only.

## ADR-001 — Regional data planes, global reference data

**Context.** The organization employs people in more than one data-protection regime. Assessment responses and evidence files are personal data. Taxonomy and published benchmarks are organizational reference data with little or no personal content.

**Decision.** Each residency zone runs its own web, API, assessment, worker, and databases for the people assigned to that zone. Published taxonomy versions and published benchmark versions replicate in as read-only reference data. Drafts stay in the authoring zone. Cross-zone dashboards use aggregates. Raw responses and evidence bytes stay where they were written.

**Consequences.** Operational cost scales with the number of zones. A global “open the person record” feature needs an explicit legal basis and a designed read path. Benchmark publication is asynchronous across zones.

**Revisit when.** Legal advice shows one zone is sufficient for every employing entity, or when a zone’s population is too small to justify a full plane and a supervised remote-read model is approved instead.

## ADR-002 — The core API is a modular monolith

**Context.** Taxonomy, profiles, benchmarks, attestation, and activation are one consistency boundary. A competency lead’s seal and the published version it seals must commit together. Assessment sittings and learning progress are a different runtime: they talk to learners in bursts and to external learning platforms.

**Decision.** One deployable, `skillhorizon-api`, with modules and a shared database, for the standard and for admission. Separate deployables are `skillhorizon-learning` (assessment, progress, external connectors) and the worker.

**Consequences.** Teams working on different modules share a release train. A bad deploy affects the whole core. That is accepted in exchange for transactional seals and simpler residency.

**Revisit when.** A module has a measured scale or compliance need that the monolith cannot meet without hurting the others. The first candidate would be readiness projection, and only if it threatens the transactional path.

## ADR-003 — The item bank stays in its own database

**Context.** The item bank and response transcripts are more sensitive than benchmark text and more sensitive than a completion message from an external learning site. The scoring path must stay free of admin edits.

**Decision.** Inside `skillhorizon-learning`, assessment has its own database, backups, and audit. Progress, including external assignment ids and completions, has a separate database. The core API authorizes a sitting and later stores the sealed result. It does not store raw responses. Appeals supersede a result. They do not mutate it.

**Consequences.** Two databases inside the learning service, plus the domain database. Sitting tokens must be designed carefully. An external connector can fail or be replaced without opening the item bank. Reporting that wants item statistics is an assessment-side product, not a SQL join from the domain database.

**Revisit when.** A formal ruling says responses must live in the same controlled record as the development path. Even then, item-bank tables stay in their own database.

## ADR-004 — Mira is later, and she is required

**Status.** Deferred in time. Not dropped.

**Context.** The employee workspace is a conversational assessment. Mira conducts the sitting and the coaching chat. That experience is built in a later session, after the framework and the system of record exist. Pack summaries for other personas can use the same assistant. Mira still must not become an unsupervised writer of career records.

**Decision.** Mira is part of the product, and she is not part of the first framework build. There is no assistant in the first shell. She gets no write privilege on benchmarks, seals, grants, or validations. The learning service records the sealed result of a sitting she conducts. A human action remains the core API command for publish, seal, and approval. Item text stays out of any general retrieval corpus.

**Consequences.** The first build leaves a service boundary where the employee conversation will sit. Gap explanation in the early slices is plain product copy. The later session adds Mira without giving her the core database role.

**Revisit when.** The employee conversational assessment is scheduled. Unsupervised writes stay out of scope.

## ADR-005 — HRIS remains the system of record for employment

**Context.** SkillHorizon needs manager, country, legal entity, and employment status for 300,000 people. Maintaining that by hand, or letting it drift from HR, breaks authorization.

**Decision.** The worker projects HR facts into SkillHorizon. SkillHorizon does not originate employment changes. The match key is the HR employee id. Records that cannot be matched are quarantined. Leavers are closed and retained, not deleted on sync.

**Consequences.** Readiness depends on feed quality. A manager relationship that is wrong in HR is wrong here until HR fixes it. Local overrides, if ever allowed, are explicit, audited, and visible as overrides.

**Revisit when.** The organization has no single HRIS and no enterprise employee id. Then the first project is an identity spine, before SkillHorizon workflow.

## ADR-006 — Cohorts are operating groups; rollups are projections

**Context.** The prototype uses one cohort per job profile. That fits a demo practice and collapses at global headcount. Competency leads still need a population view.

**Decision.** A cohort has a bounded membership inside a practice and a country or org unit, with a lead. Profile, practice, country, and org readiness are snapshots computed by the worker. Creating a cohort is not required in order to see a rollup.

**Consequences.** Two concepts to explain in the product, where the prototype had one. Wave targeting uses cohorts or explicit populations, not “everyone on this profile worldwide” unless a zone budget allows that population to be split.

**Revisit when.** Real cohort sizes in pilot are naturally small and L&D insists on a single global container. Even then, the global container is a projection, and the operating cohort stays bounded.

## ADR-007 — Ladders and profiles are configuration

**Context.** Associate, Sr. Associate, Manager, Sr. Manager, and Director are the demo ladder. A 300,000-person organization will have more than one workforce framework, and levels will change.

**Decision.** Workforce frameworks, levels, and job profiles are data. Code does not switch on those five names. Benchmarks attach to profile ids. HR’s framework code maps onto a SkillHorizon framework during sync.

**Consequences.** The first taxonomy project includes a framework import, which the prototype did not need. Screens must render levels from data.

**Revisit when.** A written workforce policy says the whole organization shares one ladder and will not add another. The data model still should not hardcode the names.

## ADR-008 — Readiness is an as-of projection

**Context.** Practice pages in the prototype derive figures from in-memory people. Doing that live across tens of thousands of people will time out and will disagree with itself mid-request.

**Decision.** Readiness snapshots are written by the worker from sealed results, the benchmark version in force, and current membership. Every displayed rollup shows its as-of time. Commands that change a score or a benchmark mark snapshots dirty; they do not recompute a practice inside the user’s request.

**Consequences.** Figures lag the seal or the score by the projection interval. The UI must say so. Debugging “why is this percent wrong” means inspecting the snapshot inputs, not re-running the page query.

**Revisit when.** A measured request path can compute the largest practice inside the latency budget. Person-level pages may compute directly earlier than practice pages. The practice and org pages stay on snapshots until that measurement exists.

## ADR-009 — Authorization is relationship and scope

**Context.** Persona labels in the prototype are a switcher on the profile. In production the same person may hold an employee workspace and a manager workspace, and must not see the rest of the world. Which of those they are working in is a session choice, described in ADR-011.

**Decision.** Every read and command of personal data is checked against HR-derived relationships and explicit SkillHorizon scopes. IdP groups may grant admin or L&D eligibility. They do not list a manager’s team. The employee workspace is granted only when personal skills or learning are assigned. Impersonation is break-glass only and does not deliver assessment items.

**Consequences.** Authorization tests are part of the domain, not a middleware afterthought. The first workforce sync must be good enough to enforce manager scope, or manager features wait. Personal-learning assignment must be explicit, or people who should only operate the system will also be dropped into an employee path.

**Revisit when.** A central entitlements service already exists and can express “manager of this person”, “lead of this practice”, and “has personal learning assigned”, with an audit trail. SkillHorizon would call it rather than reimplement the grant store. The check still happens on every request.

## ADR-011 — One active workspace, chosen only when several are held

**Status.** Accepted.

**Context.** The HTML prototype lets a person select any persona from the profile. That is how a demo walks every role with one login. In the real organization a person is one identity and may still have more than one job to do inside SkillHorizon. A people manager has direct reports to look after, and usually also has their own skills and learning assigned. An L&D operator who has no personal learning assigned has only the L&D job. Showing every persona to every login, or hiding the choice inside a profile menu, teaches the wrong model and makes it easy to act in the wrong role.

**Decision.** After corporate sign-in, SkillHorizon resolves the workspaces this person holds.

- One workspace: open it. No chooser.
- More than one: stop on an entry screen that asks which role they are entering as. The list is only the workspaces they hold. A people manager with personal learning sees “people manager” and “employee”. They do not see L&D, competency lead, or platform admin unless those grants exist too.
- The session stores that one active workspace. Navigation and commands are limited to it. Entering the other role means returning to the entry screen. The HTML profile switcher is not replicated.

**Consequences.** The same person has two sessions worth of work over a day, and the product must make returning to the entry screen obvious. Deep links carry the workspace. A link into the team, opened from a session that is currently the employee workspace, sends them through the entry screen rather than silently switching. Authorization still applies underneath: the chooser cannot grant a workspace, and a crafted request for another workspace is refused.

**Revisit when.** A single person must see their own learning and their direct reports on one screen as a deliberate combined workplace. Until that product decision exists, the workplaces stay separate.

## ADR-010 — Postgres is the transactional store

**Context.** The envelope is millions of skill rows and thousands of profiles, with a strong need for transactional publication and seals, per zone.

**Decision.** Domain Postgres for the core API. Inside the learning service, a separate assessment Postgres and a separate progress Postgres. Redis for ephemeral state. Object storage for files. A queue for the outbox. No analytical warehouse on the request path. No event-sourced primary store.

**Consequences.** Projection design has to be good, because the warehouse will not save a slow page. Operators need Postgres fluency. Cross-zone analytical needs are served by aggregate export.

**Revisit when.** A projection cannot meet the practice-page budget after indexing and snapshot design, or audit volume forces an append-only store outside Postgres. Move the projection or the audit log. Leave the transactional aggregates where they are.

## ADR-012 — Assessment and learning progress are one service, with external connectors

**Status.** Accepted.

**Context.** Learning does not all happen inside SkillHorizon. A step may be a SkillHorizon assessment, or a course on an external platform. Progress has to be recorded in one place either way, and a new external platform has to be added without folding that platform’s content into the core monolith. This is a service boundary, not a new persona workspace.

**Decision.** `skillhorizon-learning` is the service for assessments and learning progress.

- The core API decides the benchmark and who is admitted. It does not store sittings, responses, or step progress.
- The learning service runs SkillHorizon assessments and stores path progress.
- Each step names a provider. One provider is SkillHorizon assessment. Others are external learning platforms, each behind a connector that can assign an activity and accept progress or completion.
- The external platform keeps the course and the player. SkillHorizon keeps the assignment and the progress it was told.
- The item bank stays in its own database inside this service (ADR-003).

**Consequences.** The learner’s path can mix an internal assessment and an external course without a second progress store. The first release can ship with only the internal provider, and the connector port is already there. Core readiness is updated from progress events, so a practice page does not call the external platform.

**Revisit when.** An external platform must become the system of record for SkillHorizon progress. The current decision is the opposite: SkillHorizon records progress, the other site hosts the learning.

## ADR-013 — Territories and integrations are configuration

**Status.** Accepted.

**Context.** The first release brings up a few residency zones together, on the order of three or four. Later territories join one at a time. The same product must also turn an integration on for one zone without rewriting the app, and must leave a capability off in a zone that is not ready for it. Country names, legal-entity maps, and connector endpoints cannot be compiled into the services.

**Decision.** One global control registry holds no personal data. It records zones, which legal entities route to which zone, which connectors are enabled in that zone, and which capabilities are on. Every zone runs the same code and the same schemas.

Adding a territory is the same procedure every time:

1. Provision that zone from the shared infrastructure template: web, API, learning, worker, and the three PostgreSQL databases, plus Redis, storage, and the queue.
2. Register the zone in the control registry, status `provisioning`.
3. Map the legal entities that belong there. An entity with no mapping is refused at sign-in. It is never written into some other zone by default.
4. Subscribe the zone to published taxonomy and benchmark replication.
5. Turn on the HR feed for those entities, then the capabilities and connectors that zone is ready for.
6. Mark the zone `active`.

A new integration of a type the learning service already contains is a configuration row: endpoint, a reference to a secret in that zone’s secret store, and an enabled flag. A brand-new connector type, or a brand-new product service, is one code release shipped to every zone, then switched on per zone. Zones do not run different forks of the product.

**Consequences.** The first zone is built with the template, not as a one-off, so the second and the fifteenth are the same run. The control registry is a small global database and a small global service, one copy, not one per territory. Existing zones keep serving if the registry is briefly down, because each zone caches its own routing and connector config. Publishing a benchmark fans out only to zones marked active.

**Revisit when.** A territory has a legal requirement that forces a different data model, not just different config. That would be a fork, and it should be resisted. A smaller zone that should not bear a full plane is the case already described in ADR-001.

## ADR-014 — One product, deployed into the client’s environment

**Status.** Accepted.

**Context.** The first client is one organization of about 300,000 people. The same product has to be available to later clients. Each client chooses where it runs: AWS, another cloud, or their own data center. The application cannot assume Azure, a particular Kubernetes vendor, or a shared SkillHorizon cloud that holds every client’s people.

**Decision.** SkillHorizon is installed once per client. That installation contains that client’s zones, control registry, and data. Clients do not share a database.

The code depends on open protocols, not on a cloud vendor:

| Need | Protocol the code uses | Supplied by the client’s environment |
|------|------------------------|--------------------------------------|
| System of record | PostgreSQL | Any managed or on-prem Postgres |
| Session and hot sitting state | Redis | Any Redis |
| Evidence and exports | S3-compatible object storage | AWS S3, MinIO, or another S3 API |
| Jobs between services | A queue behind a small interface | RabbitMQ on-prem, or the client’s managed queue |
| Sign-in | OpenID Connect | The client’s identity provider |
| Secrets | Environment variables, or a secret-store interface | Files, Vault, or the client’s secret manager |
| Logs and traces | OpenTelemetry | Whatever collector the client runs |
| Runtime | Containers | Kubernetes anywhere, including on-prem |

Infrastructure templates may exist per environment. They are packaging. They are not branches of the application. A zone is still added by ADR-013 inside that client’s installation.

**Consequences.** The first build runs the same containers locally and in the first client’s chosen environment. Vendor SDKs stay at the edge, behind the protocols above. A second client is a new installation, with its own identity provider, HR feed, and zones. A shared multi-tenant service, where many clients live in one running system, is a different product shape and is not this decision.

**Revisit when.** A client requires a database other than PostgreSQL, or several clients must be hosted together in one installation to make the offer viable. Either case is a new ADR before any code forks.

## ADR-015 — Patterns inside the core API

**Status.** Accepted.

**Context.** The core API is one ASP.NET Core service. Modules have to stay replaceable and configurable, and the service has to run in any client environment. A large pattern catalog would hide the few rules that protect seals, grants, and published benchmarks.

**Decision.** The core follows these patterns, and declines the others named below.

| Pattern | How it is used |
|---------|----------------|
| Modular monolith | One project per bounded context, matching the domain schemas. A module exposes a small public API. Other modules do not reach into its tables |
| Ports and adapters | Only at the edge: identity provider, HR feed, queue, object storage, secret store, clock. The domain calls a port. The host supplies the adapter for that environment |
| One command, one transaction | Each use case is an application service. A seal and the benchmark it publishes commit together. There is no generic repository |
| Separate queries | Readiness and lists read stored snapshots. A command marks a snapshot dirty. It does not recompute a practice inside the request |
| Transactional outbox | The event is written in the same transaction as the change. The worker publishes it |
| Grant policy | Before a command runs, the active role and scope are checked. The screen hiding a button is not the control |
| Composition root | The host registers modules, options from environment variables, and the secret-store adapter. Business switches (territory, connector, capability) come from the control registry |
| Immutable publication | A published benchmark version is an insert. A change is a new version |

The core does not use event sourcing, a generic repository, or a distributed transaction across services. Cross-module work inside the core shares one database transaction. Mira, later, is reached through a port on the learning service, not through a reference from the core domain to a model client.

**Consequences.** A new core module is a new library plus a schema, registered in the host. Environment-specific code stays in adapters. The first framework build creates the host, the module boundaries, the grant check, the outbox, and structured logs, before feature logic.

**Revisit when.** A module must be deployed on its own because its load or trust boundary no longer fits this process. That is a new ADR, not a new pattern inside the monolith.
