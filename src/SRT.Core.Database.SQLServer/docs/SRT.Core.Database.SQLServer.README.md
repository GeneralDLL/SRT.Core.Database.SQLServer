# SRT.Core.Database.SQLServer — Documentation Index

Package: **SRT.Core.Database.SQLServer**  
Target: .NET 10 / EF Core SQL Server  
Reviewed against: SQL Server repo `c73de99`, Core repo `95b0a5b`

The GitHub homepage is the unprefixed repo-root `README.md`. This file is the **package docs index**.

All filenames in this folder start with `SRT.Core.Database.SQLServer.` so you can copy the whole set into any consumer project without colliding with other packages’ docs.

## Reading order

| Audience | Start here | Then |
|----------|------------|------|
| Humans | [SRT.Core.Database.SQLServer.HUMAN_GUIDE.md](SRT.Core.Database.SQLServer.HUMAN_GUIDE.md) | [COOKBOOK](SRT.Core.Database.SQLServer.COOKBOOK.md), [API_REFERENCE](SRT.Core.Database.SQLServer.API_REFERENCE.md) |
| AI (all tools) | [SRT.Core.Database.SQLServer.AI_RULES.md](SRT.Core.Database.SQLServer.AI_RULES.md) | [API_REFERENCE](SRT.Core.Database.SQLServer.API_REFERENCE.md), [COOKBOOK](SRT.Core.Database.SQLServer.COOKBOOK.md) |
| Codex | [SRT.Core.Database.SQLServer.AGENTS.md](SRT.Core.Database.SQLServer.AGENTS.md) | AI_RULES |
| Claude | [SRT.Core.Database.SQLServer.CLAUDE.md](SRT.Core.Database.SQLServer.CLAUDE.md) | AI_RULES |
| Cursor | Install [SRT.Core.Database.SQLServer.CURSOR_RULE.mdc](SRT.Core.Database.SQLServer.CURSOR_RULE.mdc) | AI_RULES |
| Copy paths per AI | [SRT.Core.Database.SQLServer.AI_INSTALL.md](SRT.Core.Database.SQLServer.AI_INSTALL.md) | — |

## Files in this set

1. `SRT.Core.Database.SQLServer.README.md` — this index
2. `SRT.Core.Database.SQLServer.HUMAN_GUIDE.md` — how the package works
3. `SRT.Core.Database.SQLServer.API_REFERENCE.md` — exact contracts (prefer over reading source for consumption)
4. `SRT.Core.Database.SQLServer.COOKBOOK.md` — step-by-step recipes
5. `SRT.Core.Database.SQLServer.AI_RULES.md` — canonical MUST / MUST NOT
6. `SRT.Core.Database.SQLServer.AGENTS.md` — Codex entry
7. `SRT.Core.Database.SQLServer.CLAUDE.md` — Claude entry
8. `SRT.Core.Database.SQLServer.CURSOR_RULE.mdc` — Cursor rule **template**
9. `SRT.Core.Database.SQLServer.AI_INSTALL.md` — which files to copy where for Cursor / Claude / Codex

## Copy into a consumer project

After installing / referencing `SRT.Core.Database.SQLServer`, follow **[SRT.Core.Database.SQLServer.AI_INSTALL.md](SRT.Core.Database.SQLServer.AI_INSTALL.md)** for exact destinations per AI.

Short version:

1. Copy knowledge files into `<project>/docs/` (keep package-prefixed names).
2. Cursor: also copy `.mdc` → `<project>/.cursor/rules/`.
3. Claude / Codex: copy entry file to root or `docs/`, and optionally add a one-line pointer in your main `CLAUDE.md` / `AGENTS.md`.
4. At the start of an AI session, paste the bootstrap block below if the tool did not auto-load the entry file.

## Session bootstrap (paste for Cursor / Claude / Codex)

```text
You are working with SRT.Core.Database.SQLServer.
Before changing or consuming this package:
1) Read SRT.Core.Database.SQLServer.AI_RULES.md
2) Use SRT.Core.Database.SQLServer.API_REFERENCE.md for signatures/contracts
3) Follow SRT.Core.Database.SQLServer.COOKBOOK.md for new DbContext/Repo/UoW
Rules: read docs first; do not invent APIs; if ambiguous, inspect related code and update these docs.
Cache and ErrorLog inherit DataBase but MUST NOT use BaseDBContextRepository / BaseDBContextUnitOfWork.
```

## Install Cursor rule template

Files under package `docs/` are **not** auto-loaded by Cursor.

To activate in a consumer project:

1. Copy `SRT.Core.Database.SQLServer.CURSOR_RULE.mdc` to  
   `<consumer>/.cursor/rules/SRT.Core.Database.SQLServer.CURSOR_RULE.mdc`
2. Keep the package-prefixed filename.
3. Ensure the rule’s relative links (or the bootstrap paste) still point at the copied AI_RULES file.

## Loading note

Placing `AGENTS.md` / `CLAUDE.md` only under package `docs/` does **not** guarantee automatic load in parent or consumer repos. Use the bootstrap paste and/or copy the tool entry files to the locations your tool actually reads, **keeping the package prefix** in the filename when co-located with other package docs.
