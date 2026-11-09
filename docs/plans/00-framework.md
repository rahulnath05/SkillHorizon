# 00 Framework

Parent: [Master plan](README.md)

Status: `done`

## Outcome

The same containers run locally with one zone registered. An unmapped person is refused. A log line shows the zone and the correlation id.

## Out of scope

Benchmarks, assessment items, learning paths, and Mira. No feature logic beyond the empty modules, the grant refusal, and the entry screen.

Step 00.10 is a temporary ASP.NET HTML shell (`ShellHtml.cs`, `wwwroot/shell.css`). It is not the product UI. [00a Web](00a-web.md) deletes those screens and rebuilds them in React. Do not add further interface to this host.

## Micro-steps

| Step | Work | Status |
|------|------|--------|
| 00.1 | ASP.NET Core solution with hosts for the core API, the worker, and the control registry | done |
| 00.2 | Empty module libraries matching the domain schemas: workforce, access, taxonomy, job architecture, benchmark, attestation, activation, readiness, audit | done |
| 00.3 | Versioned SQL migrations that create those schemas, plus the control schemas `zone`, `routing`, `integration`, and `capability` | done |
| 00.4 | Composition root: environment variables, a secret-store port, and module registration | done |
| 00.5 | OpenID Connect port, with a local adapter for development | done |
| 00.6 | Grant check that refuses every command until a grant exists | done |
| 00.7 | Outbox table, and a worker host that can publish a row from it | done |
| 00.8 | Structured logs through OpenTelemetry, with a correlation id and the zone on each line | done |
| 00.9 | Health endpoints on each host | done |
| 00.10 | Web shell: sign-in, and the workspace entry screen when more than one workspace is held | done |
| 00.11 | Local containers for PostgreSQL and Redis, and one registered zone | done |
| 00.12 | Prove an unmapped person is refused and a log line shows the zone and the correlation id | done |
