// Ignore Spelling: SRT app env

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SRT.Core;
using SRT.Core.Database.SQLServer.UnitOfWorks;
using SRT.Core.Databases;
using SRT.Core.Domain;
using SRT.Core.Domain.Diagnostics;
using SRT.Core.Interfaces.Databases;

namespace SRT.Core.Database.SQLServer
{
    public static class DatabaseConfigurations
    {
        #region Builder Configuration
        public static WebApplicationBuilder SRT_AddGeneralConfig<baseClassForSearchContext>(this WebApplicationBuilder builder,
                                                                 AppConnectionString connectionStrings)
        {
            var lstType = RegisterServicesByType.SRT_GetTypes<IDataBase, baseClassForSearchContext>();
            return AddGeneralConfig(builder, connectionStrings, lstType);
        }

        public static WebApplicationBuilder SRT_AddGeneralConfig(this WebApplicationBuilder builder,
                                                                 AppConnectionString connectionStrings,
                                                                 string namespacePrefixSearchInChild)
        {
            var lstType = RegisterServicesByType.SRT_GetTypes<IDataBase>(namespacePrefixSearchInChild);
            return AddGeneralConfig(builder, connectionStrings, lstType);
        }

        private static readonly HashSet<Type> RegisteredContexts = new();

        private static WebApplicationBuilder AddGeneralConfig(WebApplicationBuilder builder, AppConnectionString connectionStrings, List<Type> lstType)
        {
            foreach (var dbContextType in lstType.Where(t => typeof(DataBase).IsAssignableFrom(t)))
            {
                if (!RegisteredContexts.Add(dbContextType))
                    throw new InvalidOperationException($"DbContext {dbContextType.FullName} is already registered.");

                var optionsType = typeof(DbContextOptions<>).MakeGenericType(dbContextType);
                var options = Activator.CreateInstance(optionsType);
                var dbContextInstance = Activator.CreateInstance(dbContextType, options, connectionStrings);
                var configurationMethod = dbContextType.GetMethod("BaseConfiguration");
                configurationMethod?.Invoke(dbContextInstance, new object[] { builder, connectionStrings });
            }

            return builder;
        }
        #endregion

        #region APP Configuration
        public static async Task<WebApplication> SRT_CheckDatabase<baseClassForSearchContext>(this WebApplication app)
        {
            var lstType = RegisterServicesByType.SRT_GetTypes<IDataBase, baseClassForSearchContext>();
            return await CheckDatabase(app, lstType, app.SRT_GetLogger());
        }

        public static async Task<WebApplication> SRT_CheckDatabase(this WebApplication app,
                                                                   string namespacePrefixSearchInChild)
        {
            var lstType = RegisterServicesByType.SRT_GetTypes<IDataBase>(namespacePrefixSearchInChild);
            return await CheckDatabase(app, lstType, app.SRT_GetLogger());
        }

        private static async Task<WebApplication> CheckDatabase(WebApplication app, List<Type> lstType, ILogger logger)
        {
            var report = app.Services.GetService<SRTStartupReport>();
            foreach (Type item in lstType.Where(t => typeof(DataBase).IsAssignableFrom(t)))
            {
                string? pendingCsv = null;
                var section = "sql." + item.Name;
                var step = "connect";
                var target = "";
                var parts = new List<string>();
                try
                {
                    using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
                    {
                        var _dbContext = (DataBase)serviceScope.ServiceProvider.GetRequiredService(item);

                        var db = _dbContext.Database;
                        var contextName = _dbContext.GetType().Name;
                        var migrateTarget = DescribeSqlTarget(db.GetConnectionString());
                        target = migrateTarget;

                        var lstPending = (await db.GetPendingMigrationsAsync()).ToList();
                        pendingCsv = lstPending.Count > 0 ? string.Join(", ", lstPending) : null;

                        step = "migrate";
                        logger.LogDebug(
                            "APP - Migrate start: {Context} target={Target} pendingCount={PendingCount} pending=[{Pending}]",
                            contextName,
                            migrateTarget,
                            lstPending.Count,
                            pendingCsv ?? "none");

                        // همیشه Migrate — idempotent است؛ فقط وقتی pending>0 شرط می‌گذاشتیم،
                        // با hot-reload یا mismatch assembly گاهی pending خالی می‌ماند ولی schema عقب می‌ماند.
                        await db.MigrateAsync().ConfigureAwait(false);

                        var stillPending = (await db.GetPendingMigrationsAsync()).ToList();
                        if (stillPending.Count > 0)
                        {
                            throw new InvalidOperationException(
                                $"Migration incomplete for {contextName} ({migrateTarget}). Still pending: {string.Join(", ", stillPending)}. " +
                                "Rebuild and restart the service so the EF migration assembly matches the running binaries.");
                        }

                        var applied = await db.GetAppliedMigrationsAsync().ConfigureAwait(false);
                        var lastApplied = applied.LastOrDefault();
                        parts.Add(lstPending.Count > 0
                            ? $"migrations: applied {lstPending.Count} [{pendingCsv}] last={lastApplied ?? "(none)"}"
                            : $"migrations: up-to-date last={lastApplied ?? "(none)"}");
                        logger.LogDebug(
                            "APP - Migrate done: {Context} target={Target} lastApplied={LastApplied}",
                            contextName,
                            migrateTarget,
                            lastApplied ?? "(none)");
                    }

                    using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
                    {
                        var _dbContext = (DataBase)serviceScope.ServiceProvider.GetRequiredService(item);

                        step = "seed";
                        await _dbContext.InsertBaseData();
                        parts.Add("seed OK");

                        logger.LogDebug("APP - Insert Basic Record: {Context} - OK", _dbContext.GetType().Name);
                    }

                    bool runIdentityGapFix;
                    using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
                    {
                        var _dbContext = (DataBase)serviceScope.ServiceProvider.GetRequiredService(item);

                        step = "maintenance";
                        await _dbContext.ApplyStartupDataMaintenanceAsync();
                        runIdentityGapFix = _dbContext.RunIdentityGapFix;
                        parts.Add("maintenance OK");

                        logger.LogDebug("APP - Startup Data Maintenance: {Context} - OK", _dbContext.GetType().Name);
                    }

                    if (runIdentityGapFix)
                    {
                        step = "identity-gap";
                        var fixIdentityMethod = typeof(CheckIdentifyGap)
                            .GetMethod(nameof(CheckIdentifyGap.SRT_FixIdentityGapsAsync))
                            ?.MakeGenericMethod(item);

                        if (fixIdentityMethod is null)
                            throw new InvalidOperationException("SRT_FixIdentityGapsAsync method not found.");

                        var fixTask = fixIdentityMethod.Invoke(null, new object[] { app }) as Task;
                        if (fixTask is null)
                            throw new InvalidOperationException("Failed to invoke SRT_FixIdentityGapsAsync.");

                        await fixTask;
                        parts.Add("identity-gap fixed");
                        logger.LogDebug("APP - Identity Gap Fix: {Context} - OK", item.Name);
                    }
                    else
                    {
                        parts.Add("identity-gap skipped");
                        logger.LogDebug("APP - Identity Gap Fix skipped for {Context} (RunIdentityGapFix=false)", item.Name);
                    }

                    report?.Ok(section, target + " | " + string.Join(" | ", parts));
                }
                catch (Exception ex)
                {
                    parts.Add($"{step} FAILED: {ex.GetType().Name}: {ex.Message}");
                    if (pendingCsv is not null && step == "migrate")
                        parts.Add($"pending=[{pendingCsv}]");
                    report?.Failed(section, (string.IsNullOrEmpty(target) ? "" : target + " | ") + string.Join(" | ", parts));

                    var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
                    var conn = app.Services.GetService<AppConnectionString>()?.DefaultConnection;
                    await DiagnosticReporter.ReportAsync(
                        ex,
                        new DiagnosticEnvelope
                        {
                            Category = DiagnosticCategories.SqlMigrate,
                            Operation = "Migrate",
                            Outcome = DiagnosticOutcomes.Failed,
                            Severity = "Critical",
                            Target = conn is null ? item.Name : $"{conn.host}:{conn.port}/{conn.dbName}",
                            PersistToErrorDb = true,
                            Detail = new DiagnosticDetail
                            {
                                DbContext = item.Name,
                                PendingMigrations = pendingCsv,
                            },
                        },
                        logger,
                        scopeFactory).ConfigureAwait(false);
                    throw;
                }
            }

            return app;
        }

        private static string DescribeSqlTarget(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return "(no connection string)";

            string? server = null;
            string? database = null;
            string? user = null;
            foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0)
                    continue;

                var key = part[..idx].Trim();
                var value = part[(idx + 1)..].Trim();
                if (key.Equals("Server", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                    server = value;
                else if (key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("Database", StringComparison.OrdinalIgnoreCase))
                    database = value;
                else if (key.Equals("User ID", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("UID", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("User", StringComparison.OrdinalIgnoreCase))
                    user = value;
            }

            if (server is null && database is null)
                return "(unparsed connection)";

            // Never add the password here: this text goes to the startup summary and to telemetry.
            return user is null
                ? $"{server ?? "?"} / {database ?? "?"}"
                : $"{server ?? "?"} / {database ?? "?"} user={user}";
        }
        #endregion
    }
}