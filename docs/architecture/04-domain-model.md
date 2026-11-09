# Domain model

The prototype’s types are the start of the language: practice, competency, skill, job profile, benchmark version, cohort, person skill, report card, submission pack. Production keeps that language and tightens the boundaries the demo left implicit.

## Bounded contexts

| Context | Responsibility | System of record |
|---------|----------------|------------------|
| Workforce | People, legal entity, country, manager, employment status, workforce framework | HRIS, projected into SkillHorizon |
| Access | Which workspaces a person holds, and which one is active on the session | SkillHorizon, fed by HR relationships, personal-learning assignment, and explicit grants |
| Taxonomy | Practice, competency, skill tree, version | SkillHorizon |
| Job architecture | Ladders, levels, job profiles | SkillHorizon, aligned to HR frameworks |
| Benchmarks | Target skills and proficiencies for a profile, versioned | SkillHorizon |
| Attestation | Pack review and the competency lead’s seal | SkillHorizon |
| Activation | Cohorts and waves that admit people into measurement or development | SkillHorizon |
| Assessment | Items, sittings, responses, sealed scores | Learning service, assessment database |
| Learning progress | Path, steps, completions, evidence metadata, external-platform assignment ids | Learning service, progress database |
| Readiness | As-of snapshots for a person, team, cohort, profile, practice, country | Core API projection, fed by learning events |
| Audit | Who did what to a privileged record | SkillHorizon |

## Glossary

| Term | Meaning |
|------|---------|
| Practice | A competency domain with an accountable competency lead, such as a professional family |
| Competency | A named capability inside a practice, used to organize skills and profiles |
| Skill | A leaf of the taxonomy that a benchmark can set a target against and an assessment can measure |
| Workforce framework | A configured ladder. The demo’s Associate-to-Director ladder is one framework, not the platform |
| Job profile | A role shape inside a framework and a competency: the unit a benchmark is published against |
| Benchmark version | An immutable published set of skill targets for one job profile. A change is a new version. Status is draft, published, or superseded |
| Pack | The set of profile changes L&D submits to a competency lead for a practice |
| Seal | The competency lead’s attestation that a practice baseline may take effect |
| Cohort | An operating group of people on a shared profile and wave, small enough for a lead to run. Not a global bucket of everyone with that title |
| Wave | A capacity-bounded activation that admits a named population to baseline assessment or a development cycle |
| Sitting | One person’s SkillHorizon assessment session against a stated benchmark version |
| Sealed result | The only score record the rest of the platform may store: skill proficiencies and a result id, produced by the learning service |
| Learning step | One unit on a development path. The provider is a SkillHorizon assessment or an activity on an external learning platform |
| External completion | Progress or completion an external platform reports for a step. The course stays on that platform |
| Evidence | A file or structured reference offered to close a development step |
| Validation | A manager’s decision on evidence or a review, inside their management scope |
| Readiness snapshot | A stored figure with an as-of time. It is derived. It is not edited by hand |
| Scope | The practice, org unit, country, or relationship that limits an authority |
| Workspace | One granted way of working: Employee, People manager, Competency lead, L&D, or Platform admin. The session has one active workspace |
| Workspace entry | The screen after sign-in when the person holds more than one workspace. It lists only those workspaces |

## Aggregates

**TaxonomyVersion.** Published as a whole. Skills used by a published benchmark remain resolvable after later taxonomy edits, by version id.

**JobProfile.** Identity is stable. Display name and framework placement can change. A profile is not deleted while a benchmark or a person assignment still points at it. It is retired.

**BenchmarkVersion.** Drafts are mutable by L&D inside the authoring scope. Publication freezes the skill targets, the taxonomy version, and the effective window. Supersession points forward to the next published version. Historical sittings keep the version they were authorized against.

**AttestationPack.** Contains the proposed profile rows, market-fit classification, and exceptions. A seal records the lead, the time, and the exact pack version. An unsealed pack grants no new published baseline.

**Cohort.** Has a profile, a scope (at least practice and country or org unit), a lead, and a membership list. Membership changes are explicit. A cohort that would exceed the operating maximum is rejected; the population still appears in readiness projections.

**ActivationWave.** Names who is admitted, in which zone, against which benchmark version, and the concurrency budget. Admission is a command on the core API. The learning service opens sittings only for admitted people.

**Sitting and SealedResult.** Both live in the learning service’s assessment database. The sealed result is what progress and readiness are allowed to see.

**DevelopmentPath.** Lives in the learning service’s progress database. Built from the gap between a sealed result and the benchmark version in force. Each step names a provider: SkillHorizon assessment, or an external learning platform. Completions from that platform update the step. They do not copy the course into SkillHorizon.

**EvidenceItem and Validation.** Evidence metadata is in the progress database; bytes live in object storage. The core API authorizes the manager. The learning service stores the decision against the step they saw.

**ReadinessSnapshot.** Keyed by subject (person, team, cohort, profile, practice, country) and as-of time. Rebuilt from sealed results, published benchmarks, and membership. A stale snapshot is shown as stale. It is not silently treated as live.

## Invariants

1. A published benchmark version is immutable.
2. A sitting is authorized against one benchmark version, and the sealed result names that version.
3. Only the assessment service creates a sealed result.
4. A seal is a human action by a competency lead inside the practice scope, taken in the competency-lead workspace.
5. A manager validates only inside the management relationship that was in force for that decision, and the relationship used is stored on the validation. The session’s active workspace is People manager.
6. An employee reads their own responses only through the product rules for feedback, in the employee workspace. They do not receive the item bank, other people’s responses, or exposure statistics.
7. Personal responses and evidence bytes are stored in the person’s residency zone.
8. The employee workspace exists only when that person has personal skills or learning assigned.
9. A session names one active workspace. Commands from another workspace are refused until the person enters that workspace. The entry screen is shown only when they hold more than one.
10. Practice and country readiness figures are computed from snapshots. They do not include raw responses.
11. A leaver is closed. Their audit, sealed results, and evidence follow the retention schedule. They are not hard-deleted because a sync job ran.
12. Every publish, seal, score application, validation, export of personal data, and admin grant writes an audit record in the same transaction as the change.

## Proficiency

The prototype uses Beginner, Intermediate, Advanced, and Expert. Keep a single ordinal scale per taxonomy version so benchmarks, assessment outputs, and gaps compare. A second scale for a different framework is a new taxonomy version or an explicit mapping, not a free-text level on the person record.

## Market fit

The prototype pack rows already classify a profile as aligned, tightening, or a new bar, and they flag exceptions. That classification is part of the pack, because it changes the competency lead’s duty: aligned profiles can be batch-cleared, material shifts require joint review. It is not a field a projection job is allowed to invent after the seal.
