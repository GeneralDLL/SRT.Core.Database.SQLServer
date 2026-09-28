// Ignore Spelling: SRT app env

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SRT.Core.Database.SQLServer.UnitOfWorks;
using SRT.Core.Databases;
using SRT.Core.Domain;
using System.Xml.Linq;

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

        private static WebApplicationBuilder AddGeneralConfig(WebApplicationBuilder builder, AppConnectionString connectionStrings, List<Type> lstType)
        {
            foreach (var dbContextType in lstType)
            {
                // Create an instance of the DbContext using reflection
                var optionsType = typeof(DbContextOptions<>).MakeGenericType(dbContextType);
                var options = Activator.CreateInstance(optionsType); // You may need to pass actual options here

                var dbContextInstance = Activator.CreateInstance(dbContextType, options, connectionStrings);

                // Call the BaseConfiguration method
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
            foreach (Type item in lstType)
            {
                string? pendingCsv = null;
                try
                {
                    using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
                    {
                        var _dbContext = serviceScope.ServiceProvider.GetService(item) as DataBase;

                        if (_dbContext is null)
                            throw new Exception("db context is not existed");

                        var db = _dbContext.Database;
                        var contextName = _dbContext.GetType().Name;
                        var migrateTarget = DescribeSqlTarget(db.GetConnectionString());

                        var lstPending = (await db.GetPendingMigrationsAsync()).ToList();
                        pendingCsv = lstPending.Count > 0 ? string.Join(", ", lstPending) : null;

                        logger.LogWarning(
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
                        logger.LogWarning(
                            "APP - Migrate done: {Context} target={Target} lastApplied={LastApplied}",
                            contextName,
                            migrateTarget,
                            lastApplied ?? "(none)");
                    }

                    using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
                    {
                        var _dbContext = serviceScope.ServiceProvider.GetService(item) as DataBase;

                        if (_dbContext is null)
                            throw new Exception("db context is not existed");

                        await _dbContext.InsertBaseData();

                        logger.LogWarning($"APP - Insert Basic Record: {_dbContext.GetType().Name} - OK");
                    }

                    using (var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
                    {
                        var _dbContext = serviceScope.ServiceProvider.GetService(item) as DataBase;

                        if (_dbContext is null)
                            throw new Exception("db context is not existed");

                        await _dbContext.ApplyStartupDataMaintenanceAsync();

                        logger.LogWarning($"APP - Startup Data Maintenance: {_dbContext.GetType().Name} - OK");
                    }

                    var fixIdentityMethod = typeof(CheckIdentifyGap)
                        .GetMethod(nameof(CheckIdentifyGap.SRT_FixIdentityGapsAsync))
                        ?.MakeGenericMethod(item);

                    if (fixIdentityMethod is null)
                        throw new InvalidOperationException("SRT_FixIdentityGapsAsync method not found.");

                    var fixTask = fixIdentityMethod.Invoke(null, new object[] { app }) as Task;
                    if (fixTask is null)
                        throw new InvalidOperationException("Failed to invoke SRT_FixIdentityGapsAsync.");

                    await fixTask;
                }
                catch (Exception ex)
                {
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
            }

            if (server is null && database is null)
                return "(unparsed connection)";

            return $"{server ?? "?"} / {database ?? "?"}";
        }
        #endregion
    }
}