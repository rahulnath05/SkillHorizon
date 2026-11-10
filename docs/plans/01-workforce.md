# 01 Workforce

Parent: [Master plan](README.md)

Status: `done`

Depends on [00a Web](00a-web.md) being `done`. Screens are React, in `src/web`, and match the HTML named in that plan. Do not add product HTML to `SkillHorizon.Web`.

## Outcome

A person with only personal learning opens the employee workspace directly. An L&D operator with no personal learning opens L&D directly. A people manager who also has personal learning stops on the entry screen and can enter either workspace, and cannot enter a workspace they do not hold. In the manager workspace they see only their people. An unmapped legal entity is refused.

## Out of scope

Benchmarks, taxonomy authoring, assessment sittings, and readiness percentages. A wrong directory must not carry skill scores.

## Micro-steps

| Step | Work | Status |
|------|------|--------|
| 01.1 | HR feed port and a development adapter with a small fixture feed | done |
| 01.2 | Workforce projection keyed by employee id, including legal entity, country, manager, and employment status | done |
| 01.3 | Quarantine records that lack an employee id or collide on one | done |
| 01.4 | Close leavers. Do not delete them on sync | done |
| 01.5 | Derive manager relationships from the projection | done |
| 01.6 | Route sign-in by legal entity through the control registry. Refuse an entity with no mapping | done |
| 01.7 | Default roles and grants: employee only when personal learning is assigned, people manager from the HR relationship, L&D from an explicit grant | done |
| 01.8 | React entry screen lists only held workspaces, and only when there are two or more. Match `SkillHorizon-Shell.html` | done |
| 01.9 | Prove the three entries in React: employee only, L&D only, manager plus own learning | done |
| 01.10 | Prove a manager workspace returns only that manager’s direct reports | done |
