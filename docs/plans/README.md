# SkillHorizon build plans

Working checklists for the build. The short order is [the sequencing note](../architecture/08-sequencing.md). Decisions live in [the architecture pack](../architecture/README.md).

## Current

Next slice: [16 Assessment operations](16-assessment-operations.md).

Slices 00 through 15 are done. Slices 16 and 17 build the remaining reference screens, and the queries, commands, and tables behind them. [07 Mira](07-mira.md) stays `not-started` until 17 is done.

## Status rule

Every row on this page, every child plan, and every micro-step has one status: `not-started`, `in-progress`, or `done`.

- Starting a micro-step sets that step and its child plan to `in-progress`, and sets this page’s row for that child to `in-progress`.
- Finishing a micro-step sets only that step to `done`.
- When every micro-step in a child is `done`, that child becomes `done` and its row here becomes `done`.
- `Current` names the single next child. Only one child is `in-progress` at a time.
- 07 Mira stays `not-started` until 17 is `done`. It does not block 08 through 17.
- A later slice in 08 through 17 does not start until the previous one of that series is `done`.
- When 17 is `done`, `Current` returns to 07 Mira.

At the start of a build session, read `Current` and open that child. Do the first `not-started` micro-step. On completion, update that step, then the child, then this page, in that order. Do not mark a parent `done` while a child step is still open.

## Children

| Slice | Plan | Status |
|-------|------|--------|
| 00 Framework | [00-framework.md](00-framework.md) | done |
| 00a Web | [00a-web.md](00a-web.md) | done |
| 01 Workforce | [01-workforce.md](01-workforce.md) | done |
| 02 Benchmarks | [02-benchmarks.md](02-benchmarks.md) | done |
| 03 Learning | [03-learning.md](03-learning.md) | done |
| 04 Paths | [04-paths.md](04-paths.md) | done |
| 05 Practice | [05-practice.md](05-practice.md) | done |
| 06 Territories | [06-territories.md](06-territories.md) | done |
| 07 Mira | [07-mira.md](07-mira.md) | not-started |
| 08 Employee home | [08-employee-home.md](08-employee-home.md) | done |
| 09 Employee tasks | [09-employee-tasks.md](09-employee-tasks.md) | done |
| 10 Manager workspace | [10-manager-workspace.md](10-manager-workspace.md) | done |
| 11 Catalogue | [11-catalogue.md](11-catalogue.md) | done |
| 12 Practice review | [12-practice-review.md](12-practice-review.md) | done |
| 13 Practice picture | [13-practice-picture.md](13-practice-picture.md) | done |
| 14 Activation | [14-activation.md](14-activation.md) | done |
| 15 Assessment bank | [15-assessment-bank.md](15-assessment-bank.md) | done |
| 16 Assessment operations | [16-assessment-operations.md](16-assessment-operations.md) | not-started |
| 17 Platform admin | [17-platform-admin.md](17-platform-admin.md) | not-started |
