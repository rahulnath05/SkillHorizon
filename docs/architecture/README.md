# SkillHorizon platform architecture

Status: **proposed**. Nothing in this folder is an approved build decision until the open questions in [07-open-questions.md](./07-open-questions.md) are answered and the ADRs in [06-decisions.md](./06-decisions.md) are accepted.

Audience: platform engineering, L&D product, security, and data protection. The prototype’s screens are evidence of the workflow. These documents decide what a durable system for 300,000 people must be.

## What this pack is for

SkillHorizon makes readiness explicit. A person is measured against a published benchmark for their job profile, follows a gap-based path, submits evidence, and is validated by the people who have authority to validate. Competency leads seal practice baselines. L&D runs taxonomy, benchmarks, assessment, and activation. Platform admins run access, retention, and integrations.

Mira is required. The employee workspace is a conversational assessment she conducts. She is built in a later session, and she does not gain write access to benchmarks, seals, or approvals. That rule is ADR-004.

A person signs in once, as themselves. The product then opens the one workspace they hold, or asks them to choose when they hold more than one. That rule is ADR-011.

Assessments and learning progress run in their own service, `skillhorizon-learning`. A learning step can be a SkillHorizon assessment or an activity on an external platform. The external site hosts the course. SkillHorizon records the assignment and the progress. That rule is ADR-012.

Territories join from a shared template. The first release stands up a few zones together; later countries are registered, not forked. Integrations and capabilities are switched on per zone. That rule is ADR-013.

The product is installed once per client, in the cloud or data center that client chooses. The code speaks PostgreSQL, Redis, S3-compatible storage, OpenID Connect, and OpenTelemetry. It is not built for one vendor. That rule is ADR-014. The first installation is this organization.

The core API is ASP.NET Core. Inside it, modules follow ADR-015: one transaction per command, ports only at the edge, and published benchmarks that never change in place.

The prototype already shows that workflow for a single practice (“Data and Analytics”) with fixture data. Production has to survive a global workforce, multiple ladders, legal residency rules, and assessment integrity. That is a different system from a Vite demo.

## Reading order

| Doc | Question it answers |
|-----|---------------------|
| [01-operating-context.md](./01-operating-context.md) | Who the organization is, what SkillHorizon owns, and which prototype shortcuts must not survive |
| [02-system-context.md](./02-system-context.md) | Who uses the platform, and which corporate systems it depends on |
| [03-containers-and-data.md](./03-containers-and-data.md) | Which deployables exist, where data lives, and how a request moves |
| [04-domain-model.md](./04-domain-model.md) | The language, aggregates, and invariants |
| [05-scale-security-residency.md](./05-scale-security-residency.md) | The load, trust, and country constraints we design to |
| [06-decisions.md](./06-decisions.md) | Proposed architecture decisions, and what would reverse each one |
| [07-open-questions.md](./07-open-questions.md) | Facts we do not have, which block detailed design and build |
| [08-sequencing.md](./08-sequencing.md) | The order that protects those decisions |

## How to work in this folder

- Change an ADR’s status only when the decision is actually accepted or rejected. Leave it `Proposed` while we are still arguing.
- Put new facts in the open-questions file as answers, then update the ADR they affect. Do not hide a reversal inside a diagram.
- L1 in the prototype repo (`docs/architecture/L1-system-context.md` there) is the ancestor of [02-system-context.md](./02-system-context.md). This pack supersedes it for the production line.
- UI component design, item-writing methodology, and cloud-vendor selection are out of scope until the decisions here settle.

## Design load we are using until measured

These are planning envelopes, not production metrics. Replace them when HR and L&D supply real distributions.

| Envelope | Planning figure |
|----------|-----------------|
| Workforce identities | 300,000 active people, plus movers and leavers retained for audit |
| Skill state | About 25 current skill measurements per person (~7.5 million current rows) |
| Job profiles | Hundreds to a few thousand, not millions |
| Hottest human view | A manager’s team, or a competency lead’s practice of roughly 1,000–30,000 people |
| Assessment peak we will allow | 25,000 concurrent sittings in one residency zone |
| Assessment peak we will refuse | A single global wave of all 300,000 people |
