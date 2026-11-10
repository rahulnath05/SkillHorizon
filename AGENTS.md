## Product UI

The product interface is React in `src/web`, built with Vite. Do not add product screens as Razor, Blazor, or HTML strings in `SkillHorizon.Web`. That host keeps the session, OpenID Connect, and health, and serves the React build.

Sign-in, refusal, and workspace entry are the React screens from slice 00a. Later slices add persona screens in the same app. See `docs/plans/00a-web.md`.

The application must look like the HTML in `docs/reference/persona-htmls`.

`SkillHorizon-Combined.html` is the full picture. `SkillHorizon-Shell.html` is the chrome. Each persona file is the screen for that workspace. Match layout, type, color, and spacing. Do not copy the prototype control that lets one login become every persona. Workspace entry follows `docs/plans/README.md` and ADR-011.

## Cursor Cloud specific instructions

Cloud agents build `.cursor/Dockerfile` first. That image has the .NET 10 SDK, Node.js 22, and Docker. `dotnet restore SkillHorizon.slnx` is the install step. When `src/web/package.json` exists, the same step installs its npm dependencies. Keep service startup out of that script.

Docker must be running before the app starts. `docker compose up --build` starts Postgres, Redis, the control registry, the API, the worker, and the web shell. The first boot migrates both databases and seeds zone `local` with legal entity `LE-LOCAL`.

- Web shell: http://localhost:8080/signin
- API health: http://localhost:8081/health
- Control health: http://localhost:8082/health
- Worker health: http://localhost:8083/health

Local sign-in identities, separated by who they are rather than by workspace:

- `person-multi` holds Employee and People manager, so the entry screen is shown.
- `person-single` holds Employee only and opens that workspace directly.
- `person-unmapped` is legal entity `LE-OTHER` and is refused. The API log line includes `zone=local` and `correlation_id`.
- `person-nogrant` is mapped and has no workspace grant.
- `person-ld` holds L&D only and opens that workspace directly.

Development signing values are in `.env.example`. They are local defaults, not production secrets.

Framework tests:

```
dotnet test tests/SkillHorizon.Framework.Tests/SkillHorizon.Framework.Tests.csproj
```

The tests start their own hosts. Set `SKILLHORIZON_TEST_DOMAIN` and `SKILLHORIZON_TEST_REDIS` when host port 5432 is not the compose Postgres.
