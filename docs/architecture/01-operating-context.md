# Operating context

## The organization

SkillHorizon is for one organization of about 300,000 people, employed across many countries and legal entities. They do not share one designation ladder, one language, one work week, or one data-protection regime. They do share one workforce-readiness intent: a published definition of what a job profile requires, a measured view of where people stand, and a governed way to close the gap.

Treat “300,000” as the active workforce, not as 300,000 simultaneous users. Daily interactive use is a slice of that population: people in an active assessment or development cycle, their managers, competency leads, and L&D. The data footprint is the whole workforce. The concurrency footprint is whoever L&D activates.

## What SkillHorizon is

SkillHorizon is the system of record for:

- the published skill taxonomy and its versions
- job profiles expressed against that taxonomy
- benchmark versions, including draft, published, and superseded
- attestation of a practice baseline by the accountable competency lead
- assessment sittings and sealed scores that SkillHorizon itself administers
- development steps, evidence, and manager validation against those steps
- readiness projections computed from the above
- the audit of who changed a benchmark, a score, an attestation, or an access scope

## What stays outside

| Concern | Owner | SkillHorizon’s relationship |
|---------|--------|-----------------------------|
| Employment, legal entity, location, cost center, manager, joiner/mover/leaver | HRIS | Read and project. Match on a stable employee identifier, never on email alone |
| Authentication | Corporate identity provider | OIDC sign-in. No local passwords for employees |
| Project and staffing assignment | Staffing system | Read, so a path can respect current assignment. SkillHorizon does not staff people |
| Time and meetings | Calendar | Book slots through the worker. Calendar remains the schedule of record |
| Learning content and completions in an external catalog | Learning catalog, when one exists | Reference a step. Completions may be evidence. The catalog remains the content system of record |
| Enterprise analytics warehouse | Corporate analytics | Receive aggregates and exports. Raw responses and evidence files stay in SkillHorizon |

A skill recorded in SkillHorizon is a SkillHorizon fact. A job title recorded in HR is an HR fact. When they disagree, each system keeps its own fact and the product shows both. Silent overwrite in either direction will corrupt readiness.

## Personas and authority

Authority is a scope, not a job title painted on a screen. The same human can hold several workspaces. Each workspace is a way of working, with its own navigation and its own data.

| Workspace | When it is granted | What it is for |
|-----------|--------------------|----------------|
| Employee | The person has their own skills or learning assigned | Own sittings, own path, own evidence, own scores and gaps |
| People manager | HR records them as manager of one or more direct reports | Coaching, validation, and readiness for those reports. Cannot publish a benchmark or seal a practice |
| Competency lead | An explicit practice scope names them | Approval packs, aligned profiles, joint review when the bar moves, sealing the practice baseline |
| L&D | An explicit org or practice grant | Taxonomy, job profiles, benchmark drafts, assessment operations, activation waves |
| Platform admin | A small explicit grant | Access administration, retention, integration health. Admin rights are not a shortcut into another person’s item responses |

The employee workspace is not handed to every employed person. Someone whose job is only to run the system for others, and who has no personal learning assigned, does not receive it. A people manager usually holds both: the manager workspace for direct reports, and the employee workspace because their own skills are assigned to them as well. A competency lead or an L&D operator receives the employee workspace only when they, too, have personal learning assigned.

## How a person enters

Corporate sign-in establishes who the human is. It does not ask them to pick a persona. After sign-in, SkillHorizon resolves the workspaces that human actually holds.

- **One workspace.** The product opens it. There is no chooser. An L&D operator with no personal learning lands directly in L&D. An individual contributor with only their own skills lands directly in the employee workspace.
- **More than one workspace.** A screen in front of the product asks which role they are entering as. A people manager with their own assigned learning sees two choices: enter as a people manager, or enter as an employee working their own skills. Someone who also leads a practice sees that third choice, and no others.
- **A workspace they do not hold** is not on the screen. The HTML prototype let a person select any persona from the profile. That control was a demo. It is not part of the real solution.

The session then carries one active workspace. Navigation and commands are that workspace’s. Reading the team while entered as the employee, or opening one’s own path while entered as the manager, requires going back to the same choice screen and entering the other workspace. The identity does not change. The role in front of them does.

## Prototype shortcuts that production drops

The competency-growth prototype is the right workflow sketch and the wrong runtime model.

- **One cohort per job profile, globally.** At this headcount a “Data Analyst” profile can contain thousands of people across countries. A cohort is an operating group for a lead, a wave, and a coaching conversation. Readiness of a profile, a practice, or a country is a projection, computed and stored separately.
- **Five hardcoded designation levels.** Associate through Director matches one ladder in the demo. A global organization has more than one workforce framework. Ladders, levels, and profiles are configuration data.
- **Fixture people and in-browser state.** Employment comes from HR. Skill state, scores, and evidence are durable and audited.
- **A profile menu that can become any persona.** Production offers a choice only among workspaces that person holds, and only when they hold more than one. A manager in one country does not see a practice on another continent unless a scope grant says so.
- **Readiness computed by scanning whoever is on screen.** Practice and org figures are maintained as projections with an as-of time. A page does not aggregate 30,000 skill rows in the request.

## Product promise that architecture has to keep

A published benchmark does not change underneath a person without a new version. A score cannot be typed over a sealed result. A practice baseline is sealed by a human competency lead. A country boundary on personal data is enforced by where the bytes are stored, not by a filter in a query. A person works in one granted workspace at a time.
