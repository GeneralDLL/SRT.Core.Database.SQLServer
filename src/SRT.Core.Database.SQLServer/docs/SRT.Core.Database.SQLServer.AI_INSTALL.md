# SRT.Core.Database.SQLServer — Where to copy files for each AI

Source folder (inside this package):

`SRT.Core.Database.SQLServer/src/SRT.Core.Database.SQLServer/docs/`

Copy **with the same filenames** (keep the `SRT.Core.Database.SQLServer.` prefix).  
`<project>` = root of the consumer solution/repo that installed this package.

---

## Shared (all AIs) — human + full knowledge

Copy these into the consumer project so docs stay with the app:

| Copy from `docs/` | Paste into consumer |
|-------------------|---------------------|
| `SRT.Core.Database.SQLServer.README.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.HUMAN_GUIDE.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.API_REFERENCE.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.COOKBOOK.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.AI_RULES.md` | `<project>/docs/` |

Optional: also keep `SRT.Core.Database.SQLServer.AI_INSTALL.md` in `<project>/docs/`.

---

## Cursor

| Copy from `docs/` | Paste into consumer |
|-------------------|---------------------|
| `SRT.Core.Database.SQLServer.CURSOR_RULE.mdc` | `<project>/.cursor/rules/` |
| `SRT.Core.Database.SQLServer.AI_RULES.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.API_REFERENCE.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.COOKBOOK.md` | `<project>/docs/` |

Minimum for Cursor to apply rules automatically: the `.mdc` under `.cursor/rules/`.  
The rule points at the files under `docs/` — those three must exist there too.

---

## Claude (Claude Code / Claude projects)

| Copy from `docs/` | Paste into consumer |
|-------------------|---------------------|
| `SRT.Core.Database.SQLServer.CLAUDE.md` | `<project>/` (repo root) **or** `<project>/docs/` |
| `SRT.Core.Database.SQLServer.AI_RULES.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.API_REFERENCE.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.COOKBOOK.md` | `<project>/docs/` |

Notes:

- Claude often auto-reads a root `CLAUDE.md`. Because we keep the package prefix, prefer putting `SRT.Core.Database.SQLServer.CLAUDE.md` at repo root **and** tell Claude in the first message to follow that file, **or** add one line in your main `CLAUDE.md`:  
  `Also follow docs/SRT.Core.Database.SQLServer.AI_RULES.md`
- Do not rename to plain `CLAUDE.md` if other packages also ship Claude rules in the same repo.

---

## Codex (OpenAI Codex / AGENTS.md)

| Copy from `docs/` | Paste into consumer |
|-------------------|---------------------|
| `SRT.Core.Database.SQLServer.AGENTS.md` | `<project>/` (repo root) **or** `<project>/docs/` |
| `SRT.Core.Database.SQLServer.AI_RULES.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.API_REFERENCE.md` | `<project>/docs/` |
| `SRT.Core.Database.SQLServer.COOKBOOK.md` | `<project>/docs/` |

Notes:

- Codex looks for `AGENTS.md` at the repo root. With the package prefix, either:
  - Put `SRT.Core.Database.SQLServer.AGENTS.md` at root and mention it in the session, or
  - Add a short pointer in your root `AGENTS.md`:  
    `Follow docs/SRT.Core.Database.SQLServer.AI_RULES.md for SQL Server DB package.`
- Do not overwrite another package’s `AGENTS.md` by renaming this file to plain `AGENTS.md` when multiple packages are present.

---

## Quick matrix

| AI | Rule/entry file destination | Knowledge files destination |
|----|-----------------------------|-----------------------------|
| Cursor | `.cursor/rules/SRT.Core.Database.SQLServer.CURSOR_RULE.mdc` | `docs/SRT.Core.Database.SQLServer.{AI_RULES,API_REFERENCE,COOKBOOK}.md` |
| Claude | root or `docs/` → `SRT.Core.Database.SQLServer.CLAUDE.md` (+ optional pointer in main `CLAUDE.md`) | same `docs/` trio |
| Codex | root or `docs/` → `SRT.Core.Database.SQLServer.AGENTS.md` (+ optional pointer in main `AGENTS.md`) | same `docs/` trio |
| Humans | — | full set under `docs/` |

Canonical rules for every AI: `docs/SRT.Core.Database.SQLServer.AI_RULES.md`
