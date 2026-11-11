# Sequencing

This is the order that keeps the decisions intact. It is not a staffing plan and it has no dates. A later step that is easy to demo in the prototype waits until the step it depends on is real.

## 0. Framework, before any workflow

Build the hosts and the empty modules, not the features. One ASP.NET Core solution for the core API, the worker, and the control registry. PostgreSQL schemas created by versioned migrations. Structured logs with a correlation id. A secret-store port and environment variables. An OpenID Connect port. A grant check that refuses every command until a grant exists. An outbox table and a worker that can publish from it. A web shell that can sign in and, when a person holds more than one workspace, stop on the entry screen.

Done when the same containers run locally with one zone registered, an unmapped person is refused, and a log line shows the zone and the correlation id. No benchmarks, no items, no Mira.

The sign-in pages from this slice are a temporary .NET shell. They are replaced before any workforce screens.

## 0a. React shell

Build `src/web` with Vite and React. Delete the .NET HTML shell and serve the React build from `SkillHorizon.Web`. Sign-in, refusal, and workspace entry match `D:\Accelerators\LearningAndDevelopmentTool\skillhorizon-persona-htmls`. Later persona screens use the matching file in that folder. The profile menu that can become any persona is not copied.

## 1. Workforce spine in one zone

Stand up the control registry first, with a single zone registered in it, so the next zone is not a special case. Project HR into that residency zone. Quarantine bad identities. Derive manager relationships. Sign in with the corporate IdP, resolve employee id, and route by legal entity. An unmapped entity is refused. Resolve workspaces. Prove three entries: a person with only personal learning opens the employee workspace directly; an L&D operator with no personal learning opens L&D directly; a people manager who also has personal learning stops on the entry screen and can enter either workspace, and cannot enter a workspace they do not hold. In the manager workspace they see only their people.

No benchmarks yet. A readiness product on top of a wrong directory will attribute scores to the wrong humans.

## 2. Taxonomy, frameworks, and benchmark publication

Load workforce frameworks as data. Author a taxonomy version and publish an immutable benchmark against a job profile. Replicate that publication to the zone’s reference store. History is queryable. Drafts are not visible to employees.

This is the smallest proof of ADR-002, ADR-007, and ADR-008’s write side: one transactional API, configured ladders, immutable versions.

## 3. Learning service

Stand up `skillhorizon-learning` with the assessment database separated from the progress database. Authorize a sitting from the core API, deliver items, seal a result, and show a person-level gap. Confirm raw responses are absent from the domain database and from logs. Run a wave admission that refuses a population over the zone budget.

A path step can name a provider. The internal provider is this assessment module. The external provider interface is in place even if the first release has no external platform connected yet.

## 4. Progress, evidence, and manager validation

Build the development path in the learning service from a sealed result and the benchmark version. Store evidence metadata and bytes separately. Record a manager validation against the relationship that was in force. When an external platform is connected, a completion from that platform updates the same step record.

## 5. Packs, seals, and cohorts

L&D submits a pack. A competency lead seals it, including the aligned-versus-exception behavior from the prototype. Cohorts are created as bounded operating groups. Practice readiness is a snapshot with an as-of time, rendered for a real practice size from W1.

## 6. The first territories, then one at a time

Bring up the first three or four zones with the same template used for the first zone. Replicate published reference data. Move no responses. Prove a cross-zone aggregate, and prove that a person whose legal entity is not mapped cannot land in an existing zone.

After that, each new territory is the ADR-013 procedure: provision, register, map entities, subscribe to reference data, enable the HR feed, then enable the capabilities and connectors that territory is ready for. No country-specific build.

## 6a. Remaining reference screens, before Mira

After the first territories, build the reference screens that slices 00 through 06 left thin or did not open. The children are 08 through 17 in [the build plans](../plans/README.md). Each child adds the page, the query or command, and the table it needs.

Employee home and tasks come first, then the manager workspace, the L&D catalogue, the competency-lead review, the practice picture, activation, the assessment bank, assessment operations, and platform admin. A manager sees direct reports only. Item text and responses stay in the assessment database. An admin grant does not open a response or an evidence file. The persona switcher stays out.

Mira is still slice 07. It waits until 17 is done.

## 7. Outbound aggregates, then Mira

If A1 is answered, push aggregates to the warehouse. Mira’s conversational assessment is a later session on the learning-service boundary (ADR-004). She is not part of slices 0 through 6. The framework leaves a port for her. It does not call a model.

## What the prototype remains useful for

Use `LearningAndDevelopmentTool` to walk a workflow with L&D and competency leads: pack exceptions, benchmark journeys, person evidence, report cards. When a workflow is confirmed, write the rule into the domain model here. Do not port the fixture store, the profile persona switcher, or the single global cohort into this repository as the implementation. The entry screen in ADR-011 replaces that switcher.
