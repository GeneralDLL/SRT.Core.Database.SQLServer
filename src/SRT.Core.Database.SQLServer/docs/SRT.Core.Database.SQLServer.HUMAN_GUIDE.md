# SRT.Core.Database.SQLServer — Human Guide

This guide explains how the SQL Server package fits into an SRT host, what it registers, and how you add your own databases. For exact method signatures see [SRT.Core.Database.SQLServer.API_REFERENCE.md](SRT.Core.Database.SQLServer.API_REFERENCE.md). For copy-paste recipes see [SRT.Core.Database.SQLServer.COOKBOOK.md](SRT.Core.Database.SQLServer.COOKBOOK.md).

## What this package is

`SRT.Core.Database.SQLServer` is an SRT feature plugin (`SqlServerBasicConfigurations`, `Order = 20`, feature name `"SQLServer"`). When the host loads it, the plugin can:

- Register **ErrorLog** SQL storage (`ErrorReportDBContext` + `IErrorLogger`)
- Register **SQL-backed cache** (`CacheDBContext` + keyed `ICacheRepository` `"sql"`)
- Register **your app DbContexts** discovered by namespace (`lstDatabaseNamespace`)
- On startup: **migrate → seed → optional data maintenance → optional identity-counter fix**

It depends on `SRT.Core` (contracts, `AppSetting`, host pipeline). This package does **not** reference Redis. Dual-store order lives in `SRT.Core.Database.Redis` (`RedisSqlCacheRepository`).

## How it plugs into a host

Every SRT web host uses a thin `Program.cs`:

```csharp
var dmAppSetting = args.SRT_ConfigController();
var (builder, config) = StartAppConfigurations.SRT_ConfigController(args, dmAppSetting);
var app = await builder.SRT_ConfigBuilder(config)
    .SRT_ConfigBuilderAndBuildApp(config)
    .SRT_ConfigApp(config);
app.Run();
```

- Local DI / Swagger / middleware live in the app’s `BasicProjectConfig`, not in `Program.cs`.
- `UseAuthorization` and `MapControllers` are owned by Core — do not repeat them in the host.
- The SQL Server plugin is discovered because the host **project-references** this package (plugin loader).

Your `MyAppSetting` / `AppSetting` controls which SQL features turn on.

## Feature flags

| Flag / condition | Effect |
|------------------|--------|
| `UseErrorLogSQL == true` (default `true`) | Registers ErrorReport context + scoped `IErrorLogger` |
| `UseCashStructure == CashType.CashWithSQL` (default) | Registers Cache context + `ICacheSqlPersistence` + scoped `SqlBackedCacheRepository` + keyed `ICacheRepository` (`CacheServiceKeys.Sql`) |
| `IsSqlDatabaseRequested()` | `lstDatabaseNamespace` non-empty **and** `DefaultConnection.connectionType` empty / `sql` / `sqlserver` → registers each namespace’s `DataBase` types |

Connection slots (must be configured when the feature is on):

| Feature | Connection property |
|---------|---------------------|
| App DBs | `ConnectionStrings.DefaultConnection` |
| ErrorLog | `ConnectionStrings.SRTCore_ErrorSQL` |
| Cache SQL | `ConnectionStrings.SRTCore_CacheSQL` |

Service identity (used on `CashBackup` and ErrorLog rows) comes from Core `BindServiceIdentity`: `Service:Id` / `Service:Name` (or `ServiceId` / `ServiceName`) into `AppSetting.ServiceId` / `ServiceName`. Put that block in every environment appsettings file. Unique `Id` per microservice. Empty `Name` falls back to process friendly name. `Id` `0` is general.

## Startup pipeline (per discovered `DataBase`)

For each context type under a configured namespace:

1. **Migrate** — always `MigrateAsync()`; fails if migrations remain pending afterward
2. **`InsertBaseData()`** — your seed hook
3. **`ApplyStartupDataMaintenanceAsync()`** — optional; default no-op
4. **Identity gap fix** — only if `RunIdentityGapFix == true` on that context instance

Default on `DataBase`: `RunIdentityGapFix => false`. Domain contexts that need the fix override to `true`. Cache and ErrorLog leave the default (`false`).

Identity rules when enabled:

- Only real SQL Server IDENTITY columns (`schema.table` from metadata)
- If identity is **ahead** of `MAX(id)` → **log only** (never reseed downward at startup)
- If identity is **behind** `MAX(id)` and increment is positive → reseed **upward** only

## Architecture layers

```text
Host AppSetting / BasicProjectConfig
        │
        ▼
SqlServerBasicConfigurations  (plugin)
        │
        ├── ErrorReportDBContext + ErrorLogger     (direct DbContext; not Repo/UoW bases)
        ├── CacheDBContext + SqlBackedCacheRepository as keyed ICacheRepository "sql" (SQL-only; not Repo/UoW bases)
        └── Your AppDbContext : DataBase
                ├── YourRepository : BaseDBContextRepository<TEntity,TKey>
                └── YourUnitOfWork : BaseDBContextUnitOfWork
```

### Core contracts (in `SRT.Core`)

- `IDataBase` — provider + configuration / seed / maintenance hooks
- `IUnitOfWork` — `SaveChangesAsync`, `ExecuteInTransactionAsync`
- `IRepository<TEntity,TKey>` — GetById, AddOrUpdate, AddOrUpdateRange, DeleteByFilter
- Entity markers: `IDatabaseEntry`, `IDatabaseEntryID` / `IDatabaseEntryID<TKey>`, `IDatabaseEntryActivity`, `IDatabaseEntryAdmin`

### SQL Server bases (this package)

- `DataBase` (`SRT.Core.Databases`) — abstract `DbContext` + `IDataBase`
- `BaseDBContextRepository` / `BaseDBContextRepository<TEntity,TKey>` — CRUD helpers / `IRepository`
- `BaseDBContextUnitOfWork` — transactions + search helpers / `IUnitOfWork`

**Critical:** repositories under one unit of work must share the **same** `DataBase` instance and must **not** dispose it. DI scoped lifetime owns the context.

## Cache subsystem (do not use IRepository)

When `CashWithSQL` is on:

- `SqlBackedCacheRepository` is **SQL-only** (`CashBackup`). Ctor: `(CacheDBContext, AppSetting)` — `serviceID` / `serviceName` from `AppSetting`.
- Registered as scoped concrete type **and** keyed `ICacheRepository` with `CacheServiceKeys.Sql` (`"sql"`). Unkeyed `ICacheRepository` is **not** this type (Redis owns it).
- `ClearCategory` deletes SQL rows for this `serviceID` + category. Counters / rate-limit / backoff throw `NotSupportedException`.
- Dual-store (Redis then SQL on set; Redis then SQL on get miss + rehydrate) is in Redis `RedisSqlCacheRepository`, not here.
- Expired SQL rows are purged with `ExecuteDelete` on SQL-touching paths.

Implementations use `CacheDBContext` directly — **not** `BaseDBContextRepository`. Application code injects unkeyed `ICacheRepository` from Redis, not `CacheDBContext`.

## ErrorLog subsystem (do not use IRepository)

When `UseErrorLogSQL` is on:

- `ErrorLogger` implements `IErrorLogger`
- Search/delete use `ErrorReportFilter` (at least one non-paging criterion required)
- `MessageContains` searches the full concatenated message chunks in SQL
- Search paging: `Skip` ≥ 0; `Take` default 50, max 200
- Persist path logs via Serilog first; DB write is best-effort with a short timeout

Again: direct `ErrorReportDBContext` — **not** Repo/UoW bases.

## Adding your own database (summary)

1. Put a `DbContext : DataBase` in a namespace listed in `lstDatabaseNamespace`
2. Implement `BaseConfiguration` (`AddDbContext` + SQL Server + migrations assembly), `InsertBaseData`
3. Optionally override `RunIdentityGapFix => true`
4. Create entities with `IDatabaseEntry` + `IDatabaseEntryID` (or `<TKey>`)
5. Create repositories inheriting `BaseDBContextRepository<TEntity,TKey>` and a UoW inheriting `BaseDBContextUnitOfWork`, both injecting the **same** context type
6. Register repos/UoW as scoped in your local `SRT_ConfigBuilder`
7. Add a design-time factory if you use EF tools (see Cache/Error factories as patterns)

Details and samples: [SRT.Core.Database.SQLServer.COOKBOOK.md](SRT.Core.Database.SQLServer.COOKBOOK.md).

## Mental model for saves and deletes

| Operation | Behavior |
|-----------|----------|
| `AddOrUpdateAsync(..., autoSave: true)` | Saves **all pending changes** on the shared context |
| `AddOrUpdateRangeAsync` | Stages with `autoSave: false`, then **one** Save |
| `DeleteByFilterAsync` | Immediate `ExecuteDelete` — not deferred by autoSave |
| `ExecuteInTransactionAsync` | action → `SaveChangesAsync` → commit; on failure rollback + rethrow |

Combine bulk deletes with tracked entity changes only inside `ExecuteInTransactionAsync`.
