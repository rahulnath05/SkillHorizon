# SkillHorizon split HTML workspace

Persona experiences extracted from `Skill Horizon Updated.html` for separate editing. When you are done, run the merge script to rebuild the single-file prototype.

## Files

| File | Persona id (`persona=` / iframe key) |
|------|--------------------------------------|
| `SkillHorizon-Employee.html` | `employee` |
| `SkillHorizon-PeopleManager.html` | `manager` |
| `SkillHorizon-CompetencyLead.html` | `comlead` |
| `SkillHorizon-LearningDevelopment.html` | `ld` |
| `SkillHorizon-AssessmentEngine.html` | `engine` |
| `SkillHorizon-PlatformAdmin.html` | `admin` |
| `SkillHorizon-Shell.html` | Outer sign-in gate + iframe host (markup/CSS only) |

`manifest.json` lists ids and filenames for the merge script.

## Edit locally

Open any persona file directly in a browser. Cross-persona links that use `window.parent.postMessage({ type: 'skillhorizon:switch-persona', ... })` only work when loaded inside the shell iframe.

Fix broken cross-role links by switching persona via `postMessage` (see L&D → Assessment in `SkillHorizon-LearningDevelopment.html`).

## Merge back into one file

From the repository root:

```bash
node skillhorizon-persona-htmls/merge-skillhorizon.mjs
```

Default output: `skillhorizon-persona-htmls/SkillHorizon-Combined.html`

Optional:

```bash
node skillhorizon-persona-htmls/merge-skillhorizon.mjs --out "C:/Users/HP/Downloads/Skill Horizon Updated.html"
```

The merge script reads persona HTML from this folder, base64-encodes them into `ENCODED_DOCUMENTS`, and attaches the shell boot script from the original download (or `--source`).
