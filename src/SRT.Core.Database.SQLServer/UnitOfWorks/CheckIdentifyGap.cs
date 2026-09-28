using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SRT.Core.Database.SQLServer.UnitOfWorks
{
    internal static class CheckIdentifyGap
    {
        #region Main Method

        /// <summary>
        /// این متد:
        /// 
        /// 1- تمام جدول‌های دارای Identity را پیدا می‌کند
        /// 2- مقدار واقعی آخرین رکورد را می‌گیرد
        /// 3- مقدار فعلی Identity را از sys.identity_columns می‌گیرد
        /// 4- اگر Gap بیشتر از 100 باشد:
        ///        مقدار Identity را اصلاح می‌کند
        /// 
        /// ویژگی‌های این نسخه:
        /// 
        /// ✅ بدون Regex
        /// ✅ بدون DBCC Text Parsing
        /// ✅ Production Safe
        /// ✅ Fully Async
        /// ✅ Enterprise Style
        /// ✅ Generic
        /// ✅ سریع‌تر و تمیزتر
        /// 
        /// </summary>
        public static async Task SRT_FixIdentityGapsAsync<AppDbContext>(this WebApplication app)
            where AppDbContext : DbContext
        {
            #region Get DB Connection
            // ساخت Scope
            using var scope = app.Services.CreateScope();

            // گرفتن DbContext
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            // گرفتن Connection
            var connection = db.Database.GetDbConnection();

            // باز کردن Connection
            await connection.OpenAsync();
            #endregion

            try
            {
                #region گرفتن اطلاعات جدول‌های دارای Identity

                /*

                    sys.identity_columns

                    شامل:
                        - نام جدول
                        - نام ستون Identity
                        - آخرین مقدار Identity

                    last_value:
                        آخرین مقدار Identity تولید شده

                */

                var command = connection.CreateCommand();

                command.CommandText = @"
            SELECT
                t.name AS TableName,
                c.name AS ColumnName,
                ISNULL(ic.last_value, 0) AS CurrentIdentityValue
            FROM sys.identity_columns ic
            INNER JOIN sys.tables t
                ON ic.object_id = t.object_id
            INNER JOIN sys.columns c
                ON ic.object_id = c.object_id
                AND ic.column_id = c.column_id
            WHERE t.is_ms_shipped = 0
              AND SCHEMA_NAME(t.schema_id) NOT IN ('sys', 'INFORMATION_SCHEMA')
              AND t.name NOT LIKE 'sys%'
              AND t.name <> '__EFMigrationsHistory'
        ";

                // اجرای Query
                using var reader = await command.ExecuteReaderAsync();

                // لیست جدول ‌ها
                var tables = new List<IdentityTableInfo>();

                while (await reader.ReadAsync())
                {
                    tables.Add(new IdentityTableInfo
                    {
                        TableName = reader.GetString(0),
                        ColumnName = reader.GetString(1),
                        CurrentIdentity =
                            Convert.ToInt64(reader.GetValue(2))
                    });
                }

                await reader.CloseAsync();

                #endregion

                #region بررسی تک تک جدول ‌ها

                foreach (var table in tables)
                {
                    try
                    {
                        Console.WriteLine("------------------------------------------------");
                        Console.WriteLine($"Checking Table: {table.TableName}");

                        #region گرفتن MAX(Id)

                        /*

                            مثال:

                            SELECT ISNULL(MAX(Id),0)
                            FROM Users

                        */

                        var maxCommand = connection.CreateCommand();

                        maxCommand.CommandText = $@"
                    SELECT 
                        ISNULL(MAX([{table.ColumnName}]), 0)
                    FROM [{table.TableName}]
                ";

                        var maxObj = await maxCommand.ExecuteScalarAsync();

                        long maxId = Convert.ToInt64(maxObj);

                        Console.WriteLine($"MAX(Id) = {maxId}");

                        #endregion

                        #region گرفتن Current Identity

                        long currentIdentity = table.CurrentIdentity;

                        Console.WriteLine(
                            $"Current Identity = {currentIdentity}");

                        #endregion

                        #region محاسبه Gap

                        long gap = currentIdentity - maxId;

                        Console.WriteLine($"Gap = {gap}");

                        #endregion

                        #region بررسی نیاز به اصلاح

                        /*

                            شرط اصلاح:

                            1- Identity جلوتر باشد
                            2- Gap بیشتر از 100 باشد

                        */

                        if (currentIdentity > maxId && gap > 100)
                        {
                            // مقدار جدید

                            /*

                                مثال:

                                MAX(Id)=1200

                                مقدار جدید:

                                1210

                            */

                            long newSeed = maxId + 10;

                            Console.WriteLine(
                                $"Reseeding [{table.TableName}] To {newSeed}");

                            #region اجرای RESEED

                            var reseedCommand = connection.CreateCommand();

                            reseedCommand.CommandText = $@"
                        DBCC CHECKIDENT
                        (
                            '[{table.TableName}]',
                            RESEED,
                            {newSeed}
                        )
                    ";

                            await reseedCommand.ExecuteNonQueryAsync();

                            Console.WriteLine(
                                $"[{table.TableName}] Successfully Reseeded");

                            #endregion
                        }
                        else
                        {
                            Console.WriteLine(
                                $"No Need To Reseed [{table.TableName}]");
                        }

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Error On Table [{table.TableName}]");

                        Console.WriteLine(ex.Message);
                    }
                }

                #endregion
            }
            finally
            {
                // بستن Connection
                await connection.CloseAsync();
            }
        }

        #endregion

        #region Model

        /// <summary>
        /// اطلاعات جدول دارای Identity
        /// </summary>
        class IdentityTableInfo
        {
            /// <summary>
            /// نام جدول
            /// </summary>
            public string TableName { get; set; } = default!;

            /// <summary>
            /// نام ستون Identity
            /// </summary>
            public string ColumnName { get; set; } = default!;

            /// <summary>
            /// مقدار فعلی Identity
            /// </summary>
            public long CurrentIdentity { get; set; }

            public override string ToString()
            {
                return TableName + " - " + ColumnName + " - " + CurrentIdentity;
            }
        }

        #endregion
    }
}
