# SkillHorizon build plans

Working checklists for the build. The short order is [the sequencing note](../architecture/08-sequencing.md). Decisions live in [the architecture pack](../architecture/README.md).

## Current

Next slice: [00a Web](00a-web.md).

The framework hosts are done. The product UI is not. Slice 00a replaces the temporary .NET screens with React, matching the HTML in `D:\Accelerators\LearningAndDevelopmentTool\skillhorizon-persona-htmls`. Read that plan before writing any interface.

## Status rule

Every row on this page, every child plan, and every micro-step has one status: `not-started`, `in-progress`, or `done`.

- Starting a micro-step sets that step and its child plan to `in-progress`, and sets this page’s row for that child to `in-progress`.
- Finishing a micro-step sets only that step to `done`.
- When every micro-step in a child is `done`, that child becomes `done` and its row here becomes `done`.
- `Current` names the single active child. Only one child is `in-progress` at a time.
- A later slice does not start until the previous child is `done`.

At the start of a build session, read `Current` and open that child. Do the first `not-started` micro-step. On completion, update that step, then the child, then this page, in that order. Do not mark a parent `done` while a child step is still open.

## Children

| Slice | Plan | Status |
|-------|------|--------|
| 00 Framework | [00-framework.md](00-framework.md) | done |
| 00a Web | [00a-web.md](00a-web.md) | not-started |
| 01 Workforce | [01-workforce.md](01-workforce.md) | not-started |
| 02 Benchmarks | [02-benchmarks.md](02-benchmarks.md) | not-started |
| 03 Learning | [03-learning.md](03-learning.md) | not-started |
| 04 Paths | [04-paths.md](04-paths.md) | not-started |
| 05 Practice | [05-practice.md](05-practice.md) | not-started |
| 06 Territories | [06-territories.md](06-territories.md) | not-started |
| 07 Mira | [07-mira.md](07-mira.md) | not-started |
