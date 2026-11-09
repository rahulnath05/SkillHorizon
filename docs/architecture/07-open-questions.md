# Open questions

These are facts the architecture pack does not contain. Answering one either confirms an ADR or forces a revision. Building past the first workforce projection while the blocking set is open will bake in the wrong zones, the wrong identity key, or the wrong assessment policy.

## Blocking

| # | Question | Why it blocks | ADR it touches |
|---|----------|---------------|----------------|
| B1 | Which legal entities and countries are in the first three or four zones, and which data-residency zones does counsel require for that first wave? | Later territories use the same template (ADR-013). The first wave still chooses the real zone boundaries | ADR-001, ADR-013 |
| B2 | Is there one HRIS and one enterprise employee id for all 300,000 people, including contractors? | Without a match key, manager scope and score attribution are unsafe | ADR-005, ADR-009 |
| B3 | Is there one corporate identity provider, or federation per legal entity? | Session and joiner flow | ADR-009 |
| B4 | Are contractors, interns, and alumni in scope for assessment and retention? | Changes the population, the HR feed, and the retention classes | ADR-005 |
| B5 | What may a manager in one country see about a person employed in another? | Defines the only legal cross-zone personal read | ADR-001, ADR-009 |
| B6 | Is the benchmark global, with documented local exceptions, or can a country publish a stricter bar? | Changes reference-data replication. The prototype’s “market fit” and “tightening” rows assume a global bar plus exceptions | ADR-001, ADR-007 |
| B7 | What is the assessment policy: adaptive or fixed form, proctored or not, how often, and who may see item-level feedback? | Sizes the assessment zone and the feedback product. Cannot be retrofitted quietly onto a leaked item bank | ADR-003 |
| B8 | How long must scores, responses, evidence, and audit be kept, and who approves deletion? | Retention jobs and backup design. A guess here becomes a compliance defect | ADR-003, ADR-010 |

## Needed before L&D workflow build, not before the workforce spine

| # | Question | Why it matters |
|---|----------|----------------|
| W1 | Which practices launch first, and how large is each? | Tests the cohort and projection assumptions against a real population |
| W2 | Who is allowed to draft a benchmark, and is there a second approver besides the competency lead? | Pack workflow. The prototype has L&D submit and the lead seal |
| W3 | What happens to in-flight development paths when a benchmark version is superseded? | Gap calculation and fairness |
| W4 | Which external learning platform is first, and how does it report completion? | The connector port exists either way (ADR-012). This chooses the first adapter and the signal it sends: launch link, progress, completion |
| W5 | Is staffing data required to assign a path, or is job profile enough? | Drops or keeps the staffing feed |
| W6 | Which languages must the employee experience support at launch? | Content model for items and benchmarks. Reference data may stay in a source language longer than the employee UI |

## Needed before any export or admin console

| # | Question | Why it matters |
|---|----------|----------------|
| A1 | Which aggregates may leave the zone for the corporate warehouse? | Export contract. Default in this pack is counts and distributions only |
| A2 | Who can run a person-level export, and is it zone-local only? | High-risk command. Easy to over-grant to “admin” |
| A3 | Is there a works-council or equivalent review per country before manager dashboards go live? | Can stop a country even when the software is ready |

## Explicitly deferred

These are real, and they are not allowed to distract the blocking set.

- Packaging for a specific cloud or for an on-prem cluster. The application itself is cloud-agnostic (ADR-014).
- Visual design system for the production UI. The prototype’s Aptora Grove look is a reference, not a decision.
- Item-writing methodology and psychometric models beyond “sealed result in, gap out.”
- A shared multi-tenant installation where many clients share one running system. Each client gets their own installation (ADR-014).
- Mobile native applications. The web shell is the client until a later decision.
- Mira’s conversational assessment. Required, and scheduled after the framework (ADR-004). She does not gain write access to benchmarks, seals, or approvals.
