# SRT.Core.Database.SQLServer — API Reference

Reviewed: SQL Server repo `c73de99`, Core repo `95b0a5b`, with `DataBase.RunIdentityGapFix => false`.  
Prefer this document for consumption. If docs disagree with code, inspect the related source and update this file.

---

## 1. Plugin — `SqlServerBasicConfigurations`

- Namespace: `SRT.Core.Database.SQLServer`
- Implements: `ISRTBasicConfigurations`
- `Order` → `20`
- `FeatureName` → `SRTFeatureNames.SQLServer` (`"SQLServer"`)

### `SRT_ConfigBuilder(WebApplicationBuilder builder, AppSetting configApp)`

| Condition | Registration |
|-----------|--------------|
| `configApp.IsErrorLogSqlStore()` | `SRT_AddGeneralConfig(..., ErrorReportDBContext.Namespace)` + `AddScoped<IErrorLogger, ErrorLogger>()` |
| `configApp.IsCacheSqlStore()` | Cache namespace config + `AddSingleton<ICacheSqlPersistence>` (`CacheSqlPersistence`, `IsAvailable => true`) + `AddScoped<SqlBackedCacheRepository>()` + `AddKeyedScoped<ICacheRepository>(CacheServiceKeys.Sql, …)` |
| `configApp.IsSqlDatabaseRequested()` | For each distinct `lstDatabaseNamespace` entry: `SRT_AddGeneralConfig(connectionStrings, ns)` |

### `SRT_ConfigApp(WebApplication app, AppSetting configApp)`

Builds distinct namespace list (app DBs + ErrorLog + Cache when enabled), then `await app.SRT_CheckDatabase(ns)` for each.

---

## 2. Discovery / startup — `DatabaseConfigurations`

Namespace: `SRT.Core.Database.SQLServer`

### Builder: `SRT_AddGeneralConfig`

- Discovers non-abstract `IDataBase` implementers under a namespace prefix (or marker type’s namespace)
- Keeps types assignable to `DataBase`
- **Throws** `InvalidOperationException` if the same DbContext type is registered twice
- Instantiates `(DbContextOptions<T>, AppConnectionString)` and invokes `BaseConfiguration(builder, connectionStrings)`

### App: `SRT_CheckDatabase` (per `DataBase` type, separate scopes)

Uses `GetRequiredService(contextType)` as `DataBase`.

1. `MigrateAsync()` always; fail if pending remain
2. `InsertBaseData()`
3. `ApplyStartupDataMaintenanceAsync()`; read `RunIdentityGapFix`
4. If `RunIdentityGapFix`: `CheckIdentifyGap.SRT_FixIdentityGapsAsync<TContext>(app)`
5. On failure: `DiagnosticReporter.ReportAsync` (`Category = SqlMigrate`, `PersistToErrorDb = true`) then rethrow

---

## 3. `DataBase`

- Namespace: `SRT.Core.Databases`
- File: `DataBase.cs`
- Base: `DbContext`, implements `IDataBase`

| Member | Signature / default | Notes |
|--------|---------------------|-------|
| `ConnectionStrings` | `AppConnectionString` | Injected |
| `Provider` | `virtual DatabaseProvider Provider => SqlServer` | |
| `RunIdentityGapFix` | `virtual bool => false` | Override `true` on domain contexts that need identity maintenance |
| ctor | `(DbContextOptions options, AppConnectionString connectionStrings)` | |
| `BaseConfiguration` | `abstract void (WebApplicationBuilder, AppConnectionString)` | Register `AddDbContext` |
| `InsertBaseData` | `abstract Task` | Seed |
| `ApplyStartupDataMaintenanceAsync` | `virtual Task => CompletedTask` | Optional |

**Cache / ErrorLog:** `CacheDBContext` and `ErrorReportDBContext` inherit `DataBase` and keep default `RunIdentityGapFix = false`. They do **not** use repository/UoW bases.

---

## 4. Core contracts (`SRT.Core`)

### `IDataBase` — `SRT.Core.Interfaces.Databases`

```csharp
DatabaseProvider Provider { get; }
void BaseConfiguration(WebApplicationBuilder builder, AppConnectionString ConnectionStrings);
Task InsertBaseData();
Task ApplyStartupDataMaintenanceAsync();
```

Lifecycle (documented): register → migrate → seed → startup maintenance → optional provider identity fix.  
`RunIdentityGapFix` is **not** on `IDataBase` (SQL Server `DataBase` only).

### `IUnitOfWork`

```csharp
Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> func, CancellationToken cancellationToken = default);
```

Contract: repositories in one UoW share one context; do not dispose it.  
`ExecuteInTransactionAsync`: run delegate → `SaveChangesAsync` → commit; on error/cancel → rollback + rethrow.  
No EF types (`IDbContextTransaction`) on this interface.

### `IRepository<TEntity, TKey>`

Constraints: `where TEntity : class, IDatabaseEntry, IDatabaseEntryID<TKey>`

```csharp
Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
Task<TEntity> AddOrUpdateAsync(TEntity entity, IUserInfo? adminInfoChange = null, bool autoSave = true, CancellationToken cancellationToken = default);
Task AddOrUpdateRangeAsync(IEnumerable<TEntity> entities, IUserInfo? adminInfoChange = null, CancellationToken cancellationToken = default);
Task<int> DeleteByFilterAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
```

| Rule | Detail |
|------|--------|
| `autoSave: true` | Saves **all** pending changes on the shared context |
| `AddOrUpdateRangeAsync` | Internal `autoSave: false` per entity, then one Save |
| Default keys | Multiple `int id == 0` (or other default `TKey`) allowed in a range |
| Assigned key duplicates in one range | `ArgumentException` |
| Non-default key | Existence checked in DB (not merely `!= default`) |
| `DeleteByFilterAsync` | Immediate store delete; not deferred by autoSave |

### Entity markers — `SRT.Core.Domain.DomainInterfaces`

| Interface | Members |
|-----------|---------|
| `IDatabaseEntry` | `DateTime date_Create`, `DateTime date_Update` |
| `IDatabaseEntryID<TKey>` | `TKey id` |
| `IDatabaseEntryID` | `: IDatabaseEntryID<int>` |
| `IDatabaseEntryActivity` | `bool isActive` |
| `IDatabaseEntryAdmin` | `int? id_AdminChange`, `string? fullName_AdminChange` |
| `IUserInfo` | `: IDatabaseEntryID`; `GetFullName()`, `string userType` |

### Search / paging — `SRT.Core.Domain.BaseDomains`

**`SearchInput`**

| Property | Default / notes |
|----------|-----------------|
| `date_Start`, `date_End` | nullable |
| `isActive` | nullable |
| `OrderByDescending` | `false` |
| `orderByFieldName` | `"id"` |
| `pageIndex` | default `1`; setter clamps `<= 0` → `1` |
| `pageSize` | default `20`; setter clamps `<= 0` → `1` |

**`SearchResult<T>`** — copies paging/order from input; `count`, `lstData`, computed `totalPage`.

**`PaginationHelper`**

| Member | Behavior |
|--------|----------|
| `MaxPageSize` | `500` |
| `Validate(pageIndex, pageSize)` | `pageIndex >= 1`, `1 <= pageSize <= 500` else `ArgumentOutOfRangeException` |
| `Paginate(query, pageIndex, pageSize)` | validates then `Skip`/`Take` |
| `Paginate(query, SearchInput)` | uses `pageIndex`/`pageSize` |

---

## 5. `BaseDBContextRepository`

- Namespace: `SRT.Core.Database.SQLServer.RepositoriesBase`
- Holds `protected DataBase DbContext` — **never dispose**

### Non-generic helpers

| Method | Visibility | Behavior |
|--------|------------|----------|
| `GetByIdAsync<TEntity,TKey>(id, ct)` | protected | `AsNoTracking` |
| `AddOrUpdateAsync<TEntity,TKey>(entity, admin?, autoSave=true, ct)` | protected | Sets `date_Update`; insert if default key or id not in DB; on insert sets `date_Create` + empty `Guid` props; merges into tracked entity when present; applies `IDatabaseEntryAdmin` from `IUserInfo` (`SYSTEM` if null); `autoSave` → full context save |
| `AddOrUpdateRangeAsync<TEntity,TKey>(...)` | protected | Duplicate assigned keys → `ArgumentException`; one Save at end |
| `AddOrUpdateListAsync` | protected | `[Obsolete]` → range |
| `AddUpdateDeleteListAsync` | protected | Upsert new list; remove old-not-in-new; one Save |
| `DeleteByFilterAsync<TEntity>(predicate, ct)` | protected | Immediate `ExecuteDelete` |
| `Paginate<T>(query, pageIndex, pageSize)` | public | `PaginationHelper.Paginate` |
| `ClearChangeTracker()` | protected | Explicit only |
| `ChangeActivationAsync<TEntity>(id, isActive, admin, ct)` | public | Requires `IDatabaseEntry` + `IDatabaseEntryID` + `IDatabaseEntryActivity` |
| `ChangeActivation(...)` | public | `[Obsolete]` alias |

### `BaseDBContextRepository<TEntity, TKey> : IRepository<TEntity, TKey>`

Constraints: `class, IDatabaseEntry, IDatabaseEntryID<TKey>`  
Public forwards: `GetByIdAsync`, `AddOrUpdateAsync`, `AddOrUpdateRangeAsync`, `DeleteByFilterAsync`.

---

## 6. `BaseDBContextUnitOfWork : IUnitOfWork`

- Namespace: `SRT.Core.Database.SQLServer.UnitOfWorkBase`
- Holds `protected DataBase DbContext` — shared with repos; never dispose in repos

| Method | Behavior |
|--------|----------|
| `SaveChangesAsync(ct)` | Context save |
| `SRT_BeginTransactionAsync` / `Commit` / `Rollback` | SQL-only; prefer `ExecuteInTransactionAsync` |
| `ExecuteInTransactionAsync` (+ `TResult`) | `CreateExecutionStrategy().ExecuteAsync` → begin → func → **SaveChanges** → commit; catch → rollback + throw |
| `SRT_ExecuteInTransactionAsync*` | Legacy aliases |
| `SRT_SetPagination(query, SearchInput)` | `PaginationHelper.Paginate` |
| `SRT_OrderBy(query, SearchInput, allowedFields?)` | Default field `id`; if `allowedFields` non-null, field must be in list or `ArgumentException`; missing property → `ArgumentException`; ThenBy `id` when not the order field |
| `SRT_GenerateSearchResult(...)` | Pipeline: optional `AddInclude` → **Count** → Order(+PK) → `PaginationHelper.Validate` → page → map DTO. Caller should apply `Filter` (or other filters) **before** calling. |
| `Filter<T>(query, SearchInput)` | `where T : IDatabaseEntry, IDatabaseEntryActivity` — optional `isActive`, `date_Create >= date_Start`, `date_Create < date_End` |

`allowedFields: null` = trusted/internal path only. **User-driven** order fields must pass a whitelist.

---

## 7. Identity maintenance — `CheckIdentifyGap`

- Namespace: `SRT.Core.Database.SQLServer.UnitOfWorks` (`internal`)
- Entry: `SRT_FixIdentityGapsAsync<AppDbContext>(WebApplication app)` where `AppDbContext : DbContext`

| Rule | Behavior |
|------|----------|
| Source | `sys.identity_columns` + schema/table/column; excludes shipped / `sys*` / `__EFMigrationsHistory` |
| `currentIdentity > maxId` | Log only — **no** downward reseed at startup |
| `increment > 0` and `currentIdentity < maxId` | `DBCC CHECKIDENT` reseed to `maxId` (next value ≈ maxId + increment) |
| Per-table errors | Logged; loop continues |
| Logging | `ILogger` (not Console) |

---

## 8. Cache — public surface for consumers

Use **`ICacheRepository`** / **`ICacheSqlPersistence`** from `SRT.Core.Interfaces.Cache`. Do not wrap cache entities in `IRepository`. Hosts that also use Redis should inject **unkeyed** `ICacheRepository` (Redis / RedisSql). Do not inject `CacheDBContext` in application code.

| Type | Namespace | Role |
|------|-----------|------|
| `CacheDBContext` | `SRT.Core.Database.SQLServer.Cache` | `: DataBase`; connection `SRTCore_CacheSQL` |
| `CashBackup` | `...Cache.Domains` | Durable row; `serviceID` / `serviceName` / category / key / value (chunked) / `dateExpire` |
| `SqlBackedCacheRepository` | `...Cache` | SQL-only `ICacheRepository`; ctor `(CacheDBContext, AppSetting)` |
| `CacheServiceKeys.Sql` | `SRT.Core.Interfaces.Cache` | `"sql"` — keyed registration |
| `CacheDBContextFactory` | design-time EF | |

This package does **not** reference Redis. Dual-store order is Redis `RedisSqlCacheRepository`.

`SqlBackedCacheRepository` behavior:

- Lookup / upsert / remove / `ClearCategory` filtered by `AppSetting.ServiceId`
- `serviceName` from `AppSetting.ServiceName` (empty → process friendly name)
- Expire purge via `ExecuteDelete` on SQL-touching paths
- Counter / rate-limit / backoff methods throw `NotSupportedException` (`"Counters, rate-limit, and backoff are Redis-only and are not stored in SQL CashBackup."`)

---

## 9. ErrorLog — public surface for consumers

Use **`IErrorLogger`** (`SRT.Core.Diagnostics`).

```csharp
Task<long> Save_Error_ToDB(Exception exp);
Task<(Guid ErrorId, long DbId)> Save_Error_WithEnvelope(Exception exp, DiagnosticEnvelope envelope, bool persistToDb);
Task<long> Save_Log(string text);
string GetErrorString(Exception exp);
Task<IReadOnlyList<ErrorReportSummary>> SearchByFilterAsync(ErrorReportFilter filter, CancellationToken ct = default);
Task<int> DeleteByFilterAsync(ErrorReportFilter filter, CancellationToken ct = default);
```

**`ErrorReportFilter`** (`SRT.Core.Domain.Diagnostics`): ErrorId, CorrelationId, Category, Service, ServiceName, Environment, Operation, Severity, Outcome, ExceptionType, Target, ServiceId, DateFrom/DateTo, MessageContains, Skip, Take (default 50).

| Rule | Detail |
|------|--------|
| Empty filter | At least one non-paging criterion required or `ArgumentException` |
| Search Take | Default 50; repository clamps max **200** |
| MessageContains | Full concatenated Message1..Message5 in SQL (`nvarchar`-safe concat) |
| Delete | Same filter; `ExecuteDelete` |

`ErrorLogger` ctor takes `AppSetting`; persisted rows use `AppSetting.ServiceId` / `ServiceName` (empty name → process friendly name).

Types: `ErrorReportDBContext`, `ErrorReport`, `ErrorLogger`, design-time factory, migrations under `ErrorLog/`.

### نمایشگر لاگ خطا (پنل DevOps)

وقتی `IsErrorLogSqlStore()` درست باشد، `SRTErrorLogsController` با `ErrorLogsControllerFeatureProvider` روی همان سرویس باز می‌شود و سرویس کد جدا نمی‌خواهد. کلاس کنترلر `internal` است تا کشف پیش‌فرض MVC آن را نبیند. Auth همهٔ اکشن‌ها `CheckAuthMiddleWare("DevOps")` با `requiredAccess` است، پس هدر `X-Active-Role` لازم است. superadmin هر دو چک را رد می‌کند.

| روش و مسیر | دسترسی | بدنه | خروجی |
|---|---|---|---|
| `POST devops/error-logs/Search` | `devops.errorlogs.View` | `ErrorReportFilter` | `ErrorReportPage` |
| `GET devops/error-logs/{id}` | `devops.errorlogs.View` | ندارد | `ErrorReportDetail` یا 404 |
| `GET devops/error-logs/ByCorrelation/{correlationId}` | `devops.errorlogs.View` | ندارد | حداکثر 200 `ErrorReportSummary`، بدون بازهٔ زمانی |
| `POST devops/error-logs/Facets` | `devops.errorlogs.View` | `ErrorReportFilter` | مقدار و شمارش `Severity`، `Category`، `Outcome`، `ExceptionType`، `Environment`، `Version`، `Operation`. فقط بازهٔ زمانی اثر دارد. هر کدام حداکثر 50 مقدار |
| `POST devops/error-logs/Stats` | `devops.errorlogs.View` | `ErrorReportFilter` | `ErrorReportStats`: تا 48 ساعت سطل ساعتی، بیشتر سطل روزانه |
| `POST devops/error-logs/Export` | `devops.errorlogs.Export` | `ErrorReportFilter` | فایل `text/csv` با UTF-8 BOM، حداکثر 5000 ردیف. سلول شروع‌شده با `=`، `+`، `-` یا `@` با `'` خنثی می‌شود |
| `POST devops/error-logs/Delete` | `devops.errorlogs.Delete` | `ErrorReportFilter` | `{ deleted }`. بدون `LastHours` یا هر دو تاریخ 400 است. کاربر و فیلتر با `LogWarning` ثبت می‌شوند |

قانون‌های فیلتر:

- `Page` از 1 شروع می‌شود. `PageSize` پیش‌فرض 20 و حداکثر 100 است.
- `SortBy` برابر `date` یا `severity` است و `SortDir` پیش‌فرض `desc` است. رتبهٔ شدت: Critical، Error، Warning، بقیه.
- `LastHours` روی ساعت سرور حساب می‌شود و `DateFrom` و `DateTo` را جایگزین می‌کند، چون ساعت مرورگر ممکن است با سرور فرق کند.
- اگر هیچ بازه‌ای نیاید، Search و Facets و Stats و Export فقط 24 ساعت اخیر را می‌خوانند. Delete این پیش‌فرض را ندارد.
- `MessageContains` و `StackTraceContains` روی متن کامل تکه‌ها جستجو می‌کنند. `OperationContains` و `TargetContains` روی ستون خودشان.

متدهای نمایشگر در `IErrorLogger` پیاده‌سازی پیش‌فرض `NotSupportedException` دارند و فقط store این پکیج آن‌ها را نوشته است.

ایندکس‌های نمایشگر با migration `AddErrorReportViewerIndexes` می‌آیند: `IX_ErrorReports_date`، `IX_ErrorReports_Severity_date`، `IX_ErrorReports_Operation`.

### نگهداشت لاگ

`ErrorLogRetentionService` فقط با store SQL ثبت می‌شود. تنظیم از `SRTCore_ErrorLog:Retention` است: `Enabled` پیش‌فرض `true` و `Days` پیش‌فرض `1`. هر ساعت ردیف‌های با `date` قدیمی‌تر از `Days` روز را در دسته‌های 5000 تایی پاک می‌کند و یک لاگ Information با تعداد می‌نویسد. `Days` کمتر از 1 رد می‌شود. خطای یک اجرا با سطح Warning لاگ می‌شود و اجرای ساعت بعد ادامه دارد. لغو هنگام توقف میزبان خطا نیست.

```json
"SRTCore_ErrorLog": { "Retention": { "Enabled": true, "Days": 1 } }
```

---

## 10. AppSetting helpers (Core)

Relevant to this package (`SRT.Core.Domain.AppSetting`):

- `UseErrorLogSQL` default `true`
- `UseCashStructure` default `CashWithSQL`
- `lstDatabaseNamespace` — namespace prefixes for app `DataBase` discovery
- Empty `connectionType` is treated as SQL Server (`IsSqlServerConnectionType`)
- `IsErrorLogSqlStore()` — `UseErrorLogSQL` and `ResolveRelationalStoreType(SRTCore_ErrorSQL)` is SQL Server
- `IsCacheSqlStore()` — `CashWithSQL` and `ResolveRelationalStoreType(SRTCore_CacheSQL)` is SQL Server
- `IsSqlDatabaseRequested()` — namespaces non-empty and `DefaultConnection.connectionType` empty/`sql`/`sqlserver`
- `ServiceId` / `ServiceName` — bound by Core `BindServiceIdentity` from `Service:Id` / `Service:Name` (fallback `ServiceId` / `ServiceName`)
