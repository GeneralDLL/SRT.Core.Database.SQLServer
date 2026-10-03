using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SRT.Core.Database.SQLServer.UnitOfWorks
{
    /// <summary>
    /// Startup identity-counter maintenance for SQL Server.
    /// Never reseeds downward at startup. Gaps where currentIdentity &gt; maxId are logged only.
    /// When the counter is behind maxId and increment is positive, reseeds upward only.
    /// </summary>
    internal static class CheckIdentifyGap
    {
        #region Main Method

        public static async Task SRT_FixIdentityGapsAsync<AppDbContext>(this WebApplication app)
            where AppDbContext : DbContext
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("SRT.Core.Database.SQLServer.CheckIdentifyGap");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = db.Database.GetDbConnection();

            await connection.OpenAsync().ConfigureAwait(false);

            try
            {
                var tables = await LoadIdentityTablesAsync(connection).ConfigureAwait(false);

                foreach (var table in tables)
                {
                    try
                    {
                        await ProcessTableAsync(connection, table, logger).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Identity maintenance failed for {Schema}.{Table}",
                            table.SchemaName,
                            table.TableName);
                    }
                }
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region Load / Process

        private static async Task<List<IdentityTableInfo>> LoadIdentityTablesAsync(
            System.Data.Common.DbConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    SCHEMA_NAME(t.schema_id) AS SchemaName,
    t.name AS TableName,
    c.name AS ColumnName,
    CAST(ISNULL(ic.last_value, 0) AS bigint) AS CurrentIdentityValue,
    CAST(ic.increment_value AS bigint) AS IncrementValue
FROM sys.identity_columns ic
INNER JOIN sys.tables t
    ON ic.object_id = t.object_id
INNER JOIN sys.columns c
    ON ic.object_id = c.object_id
   AND ic.column_id = c.column_id
WHERE t.is_ms_shipped = 0
  AND SCHEMA_NAME(t.schema_id) NOT IN (N'sys', N'INFORMATION_SCHEMA')
  AND t.name NOT LIKE N'sys%'
  AND t.name <> N'__EFMigrationsHistory';
";

            var tables = new List<IdentityTableInfo>();
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                tables.Add(new IdentityTableInfo
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    ColumnName = reader.GetString(2),
                    CurrentIdentity = Convert.ToInt64(reader.GetValue(3)),
                    IncrementValue = Convert.ToInt64(reader.GetValue(4)),
                });
            }

            return tables;
        }

        private static async Task ProcessTableAsync(
            System.Data.Common.DbConnection connection,
            IdentityTableInfo table,
            ILogger logger)
        {
            var qualified = QuoteQualifiedName(table.SchemaName, table.TableName);
            var column = QuoteIdentifier(table.ColumnName);

            var maxCommand = connection.CreateCommand();
            maxCommand.CommandText = $"SELECT ISNULL(MAX({column}), 0) FROM {qualified}";
            var maxObj = await maxCommand.ExecuteScalarAsync().ConfigureAwait(false);
            var maxId = Convert.ToInt64(maxObj);
            var currentIdentity = table.CurrentIdentity;

            if (currentIdentity > maxId)
            {
                // Natural gaps (deletes/rollbacks) must not trigger downward reseed at startup.
                logger.LogInformation(
                    "Identity ahead of data (log only): {Schema}.{Table}.{Column} currentIdentity={CurrentIdentity} maxId={MaxId}",
                    table.SchemaName,
                    table.TableName,
                    table.ColumnName,
                    currentIdentity,
                    maxId);
                return;
            }

            if (table.IncrementValue > 0 && currentIdentity < maxId)
            {
                // Counter behind max: reseed upward only so the next value is maxId + increment.
                var newSeed = maxId;
                logger.LogWarning(
                    "Reseeding identity upward: {Schema}.{Table}.{Column} from {CurrentIdentity} to {NewSeed} (maxId={MaxId})",
                    table.SchemaName,
                    table.TableName,
                    table.ColumnName,
                    currentIdentity,
                    newSeed,
                    maxId);

                var reseedCommand = connection.CreateCommand();
                reseedCommand.CommandText = $"DBCC CHECKIDENT (N'{EscapeSqlLiteral(table.SchemaName)}.{EscapeSqlLiteral(table.TableName)}', RESEED, {newSeed})";
                await reseedCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                return;
            }

            logger.LogDebug(
                "Identity OK: {Schema}.{Table}.{Column} currentIdentity={CurrentIdentity} maxId={MaxId}",
                table.SchemaName,
                table.TableName,
                table.ColumnName,
                currentIdentity,
                maxId);
        }

        #endregion

        #region SQL helpers

        private static string QuoteIdentifier(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

        private static string QuoteQualifiedName(string schema, string table) =>
            $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";

        private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        #endregion

        #region Model

        private sealed class IdentityTableInfo
        {
            public string SchemaName { get; set; } = default!;
            public string TableName { get; set; } = default!;
            public string ColumnName { get; set; } = default!;
            public long CurrentIdentity { get; set; }
            public long IncrementValue { get; set; }
        }

        #endregion
    }
}
