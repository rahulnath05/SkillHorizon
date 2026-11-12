# 17 Platform admin

Parent: [Master plan](README.md)

Status: `done`

Depends on [16 Assessment operations](16-assessment-operations.md) being `done`.

## Outcome

An explicit platform-admin grant opens Access and roles, Security, Responsible AI, Audit and retention, Malpractice, Integrations, and Exports. The pages read grants, the control registry, and audit records. They do not read response bodies or evidence bytes. A grant or a personal export writes an audit row in the same transaction. With Mira unbuilt, the model register is empty.

When this child is done, `Current` in the master plan returns to [07 Mira](07-mira.md).

## Out of scope

Mira’s model client, a response-text export, changing encryption keys, and the persona switcher.

## Micro-steps

| Step | Work | Status |
|------|------|--------|
| 17.1 | Access and roles lists workspace grants and can grant or revoke one. The audit row is written with the change. The command cannot open another person’s sitting | done |
| 17.2 | Security page reads zone residency and connector status from the control registry. It does not change keys | done |
| 17.3 | Responsible AI register lists models allowed to touch a score. The register is empty while no model is connected | done |
| 17.4 | Audit page lists audit records and writes an audit row for that read. A record contains no response text and no evidence bytes | done |
| 17.5 | Malpractice case stores a status and a sitting id. It does not copy the response | done |
| 17.6 | Integrations page reads the connector registry for this zone | done |
| 17.7 | Exports page lists export types. Response text is not one of them. A personal metadata export writes an audit row | done |
