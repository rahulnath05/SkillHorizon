# 03 Learning

Parent: [Master plan](README.md)

Status: `not-started`

Depends on [02 Benchmarks](02-benchmarks.md) being `done`.

## Outcome

The learning service runs with a separate assessment database and a separate progress database. The core API authorizes a sitting. The learning service delivers items, seals a result, and shows a person-level gap. Raw responses are absent from the domain database and from logs. A wave over the zone budget is refused. A path step can name a provider. The internal provider is this assessment module. The external provider slot exists and has no platform connected yet.

## Out of scope

Evidence files, manager validation, external platform traffic, and Mira. Item text is not written to core logs.

## Micro-steps

| Step | Work | Status |
|------|------|--------|
| 03.1 | Learning service host, with assessment and progress migrations in two databases | not-started |
| 03.2 | Core API command that authorizes a sitting against one benchmark version and one admitted wave | not-started |
| 03.3 | Item delivery and response storage in the assessment database only | not-started |
| 03.4 | Seal a result. The core API stores the result id and proficiencies, not the response transcript | not-started |
| 03.5 | Person-level gap from the sealed result and the benchmark version | not-started |
| 03.6 | Wave admission refuses a population over the configured zone budget | not-started |
| 03.7 | Progress step with a provider field. One value is SkillHorizon assessment. The external value is accepted and not connected | not-started |
| 03.8 | Prove responses and item text are absent from the domain database and from logs | not-started |
