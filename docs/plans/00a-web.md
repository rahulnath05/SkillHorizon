# 00a Web

Parent: [Master plan](README.md)

Status: `not-started`

Depends on [00 Framework](00-framework.md) being `done`. The .NET hosts, databases, sign-in port, and grant check stay. This slice replaces the user interface.

## Outcome

The product UI is a React application in `src/web`. Sign-in, refusal, and the workspace entry screen look like the HTML reference below. `SkillHorizon.Web` still owns the session and OpenID Connect, and it serves the built React app. It no longer renders product screens itself.

## Look

Match the existing HTML. Do not invent a new visual system, and do not take the look from `LearningAndDevelopmentTool/apps/competency-growth`.

Reference folder:

`docs/reference/persona-htmls`

That folder is the copy a cloud agent can read. The design source on this machine is `D:\Accelerators\LearningAndDevelopmentTool\skillhorizon-persona-htmls`.

| Screen | Open this file |
|--------|----------------|
| Full product, shell plus personas | `SkillHorizon-Combined.html` |
| Sign-in gate and outer chrome | `SkillHorizon-Shell.html` |
| Employee | `SkillHorizon-Employee.html` |
| People manager | `SkillHorizon-PeopleManager.html` |
| Competency lead | `SkillHorizon-CompetencyLead.html` |
| L&D | `SkillHorizon-LearningDevelopment.html` |
| Platform admin | `SkillHorizon-PlatformAdmin.html` |
| Assessment | `SkillHorizon-AssessmentEngine.html` |

This slice builds the shell, sign-in, refusal, and the workspace entry screen. Later slices add the persona screens in the same React app, using the matching file in the table. Copy the layout, type, color, and spacing. Keep the production entry rule: a person chooses only among workspaces they hold, and only when they hold more than one. Do not copy the HTML profile menu that can become any persona.

## Delete and rebuild

These files are the .NET user interface from slice 00. Remove them. Do not extend them.

- `src/hosts/SkillHorizon.Web/ShellHtml.cs`
- `src/hosts/SkillHorizon.Web/wwwroot/shell.css`
- The page bodies in `src/hosts/SkillHorizon.Web/ShellEndpoints.cs` that return HTML

Keep health, the cookie session, OpenID Connect, and the calls that resolve workspaces. Point `/`, `/signin`, `/entry`, `/workspace`, and `/refused` at the React app.

## Out of scope

Workforce sync, benchmarks, assessment items, and Mira. Persona pages beyond the entry screen wait for their own slice.

## Micro-steps

| Step | Work | Status |
|------|------|--------|
| 00a.1 | Create `src/web` with Vite, React, and TypeScript. Carry the color, type, and spacing from `SkillHorizon-Shell.html` | not-started |
| 00a.2 | Delete `ShellHtml.cs` and `wwwroot/shell.css`. Remove the HTML responses from `ShellEndpoints.cs` | not-started |
| 00a.3 | Serve the React build from `SkillHorizon.Web`. Keep session, OpenID Connect, and health | not-started |
| 00a.4 | React sign-in and refused screens, matching the shell HTML | not-started |
| 00a.5 | React workspace entry screen, shown only when the person holds more than one workspace. One workspace opens directly | not-started |
| 00a.6 | Prove `person-multi` sees the entry screen, `person-single` does not, and `person-unmapped` is refused. The .NET HTML shell is gone | not-started |
