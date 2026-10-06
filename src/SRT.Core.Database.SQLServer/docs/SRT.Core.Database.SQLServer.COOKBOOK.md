# SRT.Core.Database.SQLServer — Cookbook

Recipes for consuming this package. Signatures match [SRT.Core.Database.SQLServer.API_REFERENCE.md](SRT.Core.Database.SQLServer.API_REFERENCE.md). Replace `YourApp.*` names with your project’s.

---

## 1. Host startup (self-contained)

### `Program.cs` (web host — keep thin)

```csharp
var dmAppSetting = args.SRT_ConfigController();
var (builder, config) = StartAppConfigurations.SRT_ConfigController(args, dmAppSetting);
var app = await builder.SRT_ConfigBuilder(config)
    .SRT_ConfigBuilderAndBuildApp(config)
    .SRT_ConfigApp(config);
app.Run();
```

Do **not** call `UseAuthorization` / `MapControllers` here (Core owns them). Do **not** register SQL Server DI in `Program.cs`.

### `BasicProjectConfig` — enable SQL + your DbContext namespace

```csharp
public static class BasicProjectConfig
{
    public static MyAppSetting SRT_ConfigController(this string[] args)
    {
        return new MyAppSetting
        {
            // App DbContexts live under this namespace prefix
            lstDatabaseNamespace = { "YourApp.Database" },

            UseErrorLogSQL = true,
            UseCashStructure = CashType.CashWithSQL,

            ConnectionStrings = new AppConnectionString
            {
                DefaultConnection = new GeneralConnectionString { /* host, port, dbName, user, pass, connectionType = "sqlserver" or empty */ },
                SRTCore_ErrorSQL = new GeneralConnectionString { /* inherit type from Default if empty */ },
                SRTCore_CacheSQL = new GeneralConnectionString { /* inherit type from Default if empty */ },
            },
            // Plugin gates: IsErrorLogSqlStore() / IsCacheSqlStore() / IsSqlDatabaseRequested()
        };
    }

    public static WebApplicationBuilder SRT_ConfigBuilder(this WebApplicationBuilder builder, MyAppSetting config)
    {
        // Local DI only — plugin registers DbContexts via SqlServerBasicConfigurations
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<IProductUnitOfWork, ProductUnitOfWork>();
        return builder;
    }

    public static async Task<WebApplication> SRT_ConfigApp(this Task<WebApplication> appTask, MyAppSetting config)
    {
        var app = await appTask;
        return app.SRT_ConfigApp(config);
    }

    public static WebApplication SRT_ConfigApp(this WebApplication app, MyAppSetting config)
    {
        // Local middleware only
        return app;
    }
}
```

Ensure the **Web** project references `SRT.Core.Database.SQLServer` so the plugin is discovered.

Add `Service` to **every** environment appsettings file (unique `Id` per microservice). Cache SQL rows and ErrorLog use these values:

```json
"Service": {
  "Id": 9001,
  "Name": "YourApp.Web"
}
```

---

## 2. App DbContext

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SRT.Core.Databases;
using SRT.Core.Domain;

namespace YourApp.Database;

public sealed class AppDbContext : DataBase
{
    public DbSet<Product> Products => Set<Product>();

    // Enable only if this database should run startup identity maintenance
    public override bool RunIdentityGapFix => true;

    public AppDbContext(DbContextOptions<AppDbContext> options, AppConnectionString connectionStrings)
        : base(options, connectionStrings)
    {
    }

    public override void BaseConfiguration(WebApplicationBuilder builder, AppConnectionString connectionStrings)
    {
        builder.Services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(
                connectionStrings.DefaultConnection.GetConnectionString(),
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name)));
    }

    public override Task InsertBaseData() => Task.CompletedTask;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(x => x.id);
            e.Property(x => x.name).HasMaxLength(200).IsRequired();
        });
        base.OnModelCreating(modelBuilder);
    }
}
```

Put this type under the namespace listed in `lstDatabaseNamespace` (`YourApp.Database` above).

### Design-time factory (EF tools)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SRT.Core.Domain;

namespace YourApp.Database;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = new AppConnectionString
        {
            DefaultConnection = new GeneralConnectionString
            {
                // design-time values or env-based
                connectionType = "sqlserver",
            }
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(cs.DefaultConnection.GetConnectionString())
            .Options;
        return new AppDbContext(options, cs);
    }
}
```

---

## 3. Entity markers

```csharp
using SRT.Core.Domain.DomainInterfaces;

namespace YourApp.Database;

public class Product : IDatabaseEntry, IDatabaseEntryID, IDatabaseEntryActivity, IDatabaseEntryAdmin
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public bool isActive { get; set; } = true;
    public DateTime date_Create { get; set; }
    public DateTime date_Update { get; set; }
    public int? id_AdminChange { get; set; }
    public string? fullName_AdminChange { get; set; }
}
```

For non-`int` keys use `IDatabaseEntryID<Guid>` (or other `TKey`) instead of non-generic `IDatabaseEntryID`.

---

## 4. Repository

```csharp
using SRT.Core.Database.SQLServer.RepositoriesBase;
using SRT.Core.Interfaces.Databases;

namespace YourApp.Database;

public interface IProductRepository : IRepository<Product, int>
{
    // app-specific queries if needed
}

public sealed class ProductRepository : BaseDBContextRepository<Product, int>, IProductRepository
{
    public ProductRepository(AppDbContext dbContext) : base(dbContext)
    {
    }
}
```

### Usage notes

```csharp
// Insert (id == 0)
await _products.AddOrUpdateAsync(new Product { name = "A" }, admin);

// Update — non-default id that exists in DB
await _products.AddOrUpdateAsync(existing, admin, autoSave: true);

// Range — multiple id==0 OK; duplicate assigned ids throw ArgumentException
await _products.AddOrUpdateRangeAsync(list, admin);

// Immediate bulk delete (not deferred by autoSave)
await _products.DeleteByFilterAsync(p => !p.isActive, ct);
```

---

## 5. Unit of work (shared context)

```csharp
using SRT.Core.Database.SQLServer.UnitOfWorkBase;
using SRT.Core.Domain.BaseDomains;
using SRT.Core.Interfaces.Databases;

namespace YourApp.Database;

public interface IProductUnitOfWork : IUnitOfWork
{
    IProductRepository Products { get; }
    Task<ProductSearchResult> SearchAsync(ProductSearchInput input, CancellationToken ct = default);
}

public sealed class ProductUnitOfWork : BaseDBContextUnitOfWork, IProductUnitOfWork
{
    public IProductRepository Products { get; }

    public ProductUnitOfWork(AppDbContext dbContext, IProductRepository products) : base(dbContext)
    {
        // products MUST be constructed with the same AppDbContext instance (scoped DI)
        Products = products;
    }

    private static readonly string[] AllowedOrderFields = { "id", "name", "date_Create" };

    public async Task<ProductSearchResult> SearchAsync(ProductSearchInput input, CancellationToken ct = default)
    {
        IQueryable<Product> query = DbContext.Set<Product>().AsNoTracking();
        query = Filter(query, input); // isActive / date range from SearchInput

        var result = new ProductSearchResult(input);
        return await SRT_GenerateSearchResult(
            query,
            input,
            result,
            p => new ProductDto { Id = p.id, Name = p.name },
            AddInclude: null,
            allowedOrderFields: AllowedOrderFields, // required for user input
            cancellationToken: ct);
    }
}
```

Register both as **scoped** so they share one `AppDbContext` per request:

```csharp
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IProductUnitOfWork, ProductUnitOfWork>();
```

---

## 6. Transactions

```csharp
await uow.ExecuteInTransactionAsync(async () =>
{
    await uow.Products.AddOrUpdateAsync(entity, admin, autoSave: false);
    // more tracked work...
    // SaveChanges runs automatically before commit inside ExecuteInTransactionAsync
}, ct);
```

### Bulk + tracked together

`DeleteByFilterAsync` / `ExecuteDelete` run **immediately**. To combine with tracked changes:

```csharp
await uow.ExecuteInTransactionAsync(async () =>
{
    await uow.Products.DeleteByFilterAsync(p => p.name == "gone", ct);
    await uow.Products.AddOrUpdateAsync(replacement, admin, autoSave: false);
}, ct);
```

---

## 7. Search input / result types

```csharp
using SRT.Core.Domain.BaseDomains;

public sealed class ProductSearchInput : SearchInput
{
    public string? NameContains { get; set; }
}

public sealed class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class ProductSearchResult : SearchResult<ProductDto>
{
    public ProductSearchResult(SearchInput input) : base(input) { }
}
```

Pagination: after order, `PaginationHelper` enforces `pageIndex >= 1` and `1 <= pageSize <= 500`.

Apply custom filters **before** `SRT_GenerateSearchResult` (the helper’s `Filter` only covers `isActive` / create date range).

---

## 8. Identity gap fix opt-in

```csharp
public override bool RunIdentityGapFix => true; // on your DataBase subclass only
```

Leave `false` (default) for Cache/ErrorLog and for DBs that must not touch identity counters.

Startup never reseeds **downward**. Large gaps after deletes are logged, not “fixed” by lowering the counter.

---

## 9. Cache & ErrorLog as a consumer

### Cache

```csharp
// Inject unkeyed ICacheRepository (Redis / RedisSql). Do not inject CacheDBContext.
await cache.AddNewData("cat", "key", value, expireUtc);
var item = await cache.GetData<MyDto>("cat", "key");
await cache.ClearCategory("cat");
```

SQL-only store is keyed `CacheServiceKeys.Sql`. This package does not wrap Redis. For Redis+SQL dual-store, reference `SRT.Core.Database.Redis` with `CashType.CashWithSQL`.

### ErrorLog

```csharp
// Inject IErrorLogger
await errorLogger.Save_Error_ToDB(ex);

var rows = await errorLogger.SearchByFilterAsync(new ErrorReportFilter
{
    ServiceName = "MyService",
    MessageContains = "timeout",
    Take = 50,
}, ct);
```

At least one non-paging filter field is required. Do not route ErrorReport through app UoW/`IRepository`.

---

## 10. Checklist for a new SQL feature in an app

1. ProjectReference `SRT.Core.Database.SQLServer`
2. Set connection strings + `lstDatabaseNamespace`
3. Add `DataBase` subclass in that namespace
4. Entities implement marker interfaces
5. Scoped Repo + UoW sharing the same context
6. User search: whitelist order fields
7. Bulk + tracked: only inside `ExecuteInTransactionAsync`
8. Copy this `docs/` set (prefixed filenames) into the consumer project for humans/AI
