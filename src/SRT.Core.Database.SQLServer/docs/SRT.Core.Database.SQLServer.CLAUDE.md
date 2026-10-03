# SRT.Core.Database.SQLServer — Claude entry

Package: **SRT.Core.Database.SQLServer**

## Required reading

- [SRT.Core.Database.SQLServer.AI_RULES.md](SRT.Core.Database.SQLServer.AI_RULES.md) — follow entirely
- [SRT.Core.Database.SQLServer.API_REFERENCE.md](SRT.Core.Database.SQLServer.API_REFERENCE.md) — signatures and contracts
- [SRT.Core.Database.SQLServer.COOKBOOK.md](SRT.Core.Database.SQLServer.COOKBOOK.md) — implementation recipes

## Working rule

Read docs first. Do not invent APIs. If ambiguous, inspect related code and update these docs.

Cache and ErrorLog inherit `DataBase` but must not use `BaseDBContextRepository` / `BaseDBContextUnitOfWork`. SQL cache is keyed `CacheServiceKeys.Sql` (SQL-only); unkeyed `ICacheRepository` is Redis.

Keep the `SRT.Core.Database.SQLServer.` filename prefix when copying into consumer projects. This entry file is a pointer only; `AI_RULES.md` is authoritative.
