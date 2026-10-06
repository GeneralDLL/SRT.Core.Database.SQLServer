# SRT.Core.Database.SQLServer

EF Core SQL Server plugin for SRT hosts: app `DbContext` discovery, ErrorLog, and SQL-backed cache.

**Target:** .NET 10

Package docs (nine prefixed files): **[src/SRT.Core.Database.SQLServer/docs/SRT.Core.Database.SQLServer.README.md](src/SRT.Core.Database.SQLServer/docs/SRT.Core.Database.SQLServer.README.md)**

## Consume

```xml
<ProjectReference Include="..\SRT.Core.Database.SQLServer\src\SRT.Core.Database.SQLServer\SRT.Core.Database.SQLServer.csproj" />
```

Also reference **SRT.Core**. Registration uses `IsErrorLogSqlStore()` / `IsCacheSqlStore()` / `IsSqlDatabaseRequested()` (empty `connectionType` means SQL Server).

## Quick start

Project-reference this package so `SqlServerBasicConfigurations` is discovered. List app DbContext namespaces in `AppSetting.lstDatabaseNamespace`. Configure `ConnectionDB:DefaultConnection`, `SRTCore_ErrorSQL`, and `SRTCore_CacheSQL` as needed.

Host `Program.cs` stays the Core thin bootstrap. Recipes: [COOKBOOK](src/SRT.Core.Database.SQLServer/docs/SRT.Core.Database.SQLServer.COOKBOOK.md).
