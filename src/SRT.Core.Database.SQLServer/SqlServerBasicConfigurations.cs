using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SRT.Core.Database.SQLServer.ErrorLog;
using SRT.Core.Database.SQLServer.ErrorLog.Controllers;
using SRT.Core.Database.SQLServer.ErrorLog.Repository;
using SRT.Core.Database.SQLServer.ErrorLog.Retention;
using SqlCache = SRT.Core.Database.SQLServer.Cache;
using SRT.Core.Domain;
using SRT.Core.Interfaces;
using SRT.Core.SystemEnums;
using SRT.Core.Interfaces.Cache;
using SRT.Core.Diagnostics;

namespace SRT.Core.Database.SQLServer
{
    /// <summary>
    /// Registers SQL Server feature services (ErrorLog, Cache, app DbContexts) and runs
    /// migrate / seed / optional identity maintenance at app startup.
    /// Registration behavior is unchanged by the main-DB refactor; bases
    /// (<c>BaseDBContextRepository</c>, <c>BaseDBContextUnitOfWork</c>) are available for
    /// domain contexts and are not used by Cache/ErrorLog in this phase.
    /// </summary>
    public class SqlServerBasicConfigurations : ISRTBasicConfigurations
    {
        public int Order => 20;
        public string FeatureName => SRTFeatureNames.SQLServer;

        #region Builder

        public Task<WebApplicationBuilder> SRT_ConfigBuilder(WebApplicationBuilder builder, AppSetting configApp)
        {
            #region ErrorLog SQL
            if (configApp.IsErrorLogSqlStore())
            {
                builder.SRT_AddGeneralConfig(configApp.ConnectionStrings, typeof(ErrorReportDBContext).Namespace!);
                builder.Services.AddScoped<IErrorLogger, ErrorLogger>();
                builder.Services.AddControllers().ConfigureApplicationPartManager(
                    manager => manager.FeatureProviders.Add(new ErrorLogsControllerFeatureProvider()));
                builder.Services.Configure<ErrorLogRetentionOptions>(builder.Configuration.GetSection(ErrorLogRetentionOptions.SectionName));
                builder.Services.AddHostedService<ErrorLogRetentionService>();
            }
            #endregion

            #region Cache SQL
            if (configApp.IsCacheSqlStore())
            {
                builder.SRT_AddGeneralConfig(configApp.ConnectionStrings, typeof(SqlCache.CacheDBContext).Namespace!);
                builder.Services.AddSingleton<ICacheSqlPersistence>(_ => new CacheSqlPersistence());
                builder.Services.AddScoped<SqlCache.SqlBackedCacheRepository>();
                builder.Services.AddKeyedScoped<ICacheRepository>(
                    CacheServiceKeys.Sql,
                    (sp, _) => sp.GetRequiredService<SqlCache.SqlBackedCacheRepository>());
            }
            #endregion

            #region App database namespaces
            if (configApp.IsSqlDatabaseRequested())
            {
                foreach (var ns in configApp.lstDatabaseNamespace.Distinct())
                    builder.SRT_AddGeneralConfig(configApp.ConnectionStrings, ns);
            }
            #endregion

            return Task.FromResult(builder);
        }

        #endregion

        #region App

        public async Task<WebApplication> SRT_ConfigApp(WebApplication app, AppSetting configApp)
        {
            var namespaces = new HashSet<string>();

            if (configApp.IsSqlDatabaseRequested())
                configApp.lstDatabaseNamespace.ForEach(r => namespaces.Add(r));

            if (configApp.IsErrorLogSqlStore())
                namespaces.Add(typeof(ErrorReportDBContext).Namespace!);

            if (configApp.IsCacheSqlStore())
                namespaces.Add(typeof(SqlCache.CacheDBContext).Namespace!);

            foreach (var ns in namespaces)
                await app.SRT_CheckDatabase(ns);

            return app;
        }

        #endregion

        #region Nested

        private sealed class CacheSqlPersistence : ICacheSqlPersistence
        {
            public bool IsAvailable => true;
        }

        #endregion
    }
}
