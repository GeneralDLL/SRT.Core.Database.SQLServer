using SRT.Core.Databases;
using SRT.Core.Database.SQLServer.Cache.Domains;
using SRT.Core.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SRT.Core.Database.SQLServer.Cache
{
    public class CacheDBContext : DataBase
    {
        public DbSet<CashBackup> CashBackup { get; set; }

        #region Constructors
        public CacheDBContext(DbContextOptions<CacheDBContext> options,
                             AppConnectionString ConnectionStrings)
        : base(options, ConnectionStrings)
        {
        }
        #endregion

        #region On Configuration => اگر کانفیگ نشده بود جهت تست - کانفیگ دیفالت برایش ست کند
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                Console.WriteLine("Base Configuration -------------------- Attention");

                var connection = new GeneralConnectionString()
                {
                    connectionType = "SQL",
                    dbName = "CacheDatabase",
                    host = "localhost",
                    port = "11433",
                    user = "sa",
                    pass = "SaeedTNT220"
                };

                // Design-time fallback only (no DI options). No SQL command logging and no sensitive data logging here:
                // a library must not print parameter values.
                optionsBuilder.UseSqlServer(connection.GetConnectionString());
            }
        }
        #endregion

        #region Base Function
        public override void BaseConfiguration(WebApplicationBuilder builder, AppConnectionString ConnectionStrings)
        {
            // Tracking enabled so upserts/updates in SqlBackedCacheRepository persist on SaveChanges.
            builder.Services.AddDbContext<CacheDBContext>(options =>
                options.UseSqlServer(
                    ConnectionStrings.SRTCore_CacheSQL.GetConnectionString(),
                    sql => sql.MigrationsAssembly(typeof(CacheDBContext).Assembly.GetName().Name)));
        }

        public override async Task InsertBaseData()
        {
            // Full-text index is not expressible in EF fluent API; create it once at DB bootstrap.
            // Requires SQL Server Full-Text Search feature + unique key (PK on id).
            await Database.ExecuteSqlRawAsync("""
                IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'CashBackupCatalog')
                        CREATE FULLTEXT CATALOG CashBackupCatalog AS DEFAULT;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM sys.fulltext_indexes fi
                        INNER JOIN sys.tables t ON fi.object_id = t.object_id
                        WHERE t.name = N'CashBackup')
                    BEGIN
                        DECLARE @pk sysname =
                        (
                            SELECT i.name
                            FROM sys.indexes i
                            INNER JOIN sys.tables t ON i.object_id = t.object_id
                            WHERE t.name = N'CashBackup' AND i.is_primary_key = 1
                        );

                        IF @pk IS NOT NULL
                            EXEC(N'CREATE FULLTEXT INDEX ON dbo.CashBackup([key] LANGUAGE 1033)
                                   KEY INDEX [' + @pk + N'] ON CashBackupCatalog
                                   WITH CHANGE_TRACKING AUTO');
                    END
                END
                """);
        }
        #endregion

        #region On Model Creating
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Mirrors CashBackup comments: index serviceID + key; unique (serviceID, category, key); FTS on key via InsertBaseData
            modelBuilder.Entity<CashBackup>(entity =>
            {
                entity.HasKey(e => e.id);

                entity.Property(e => e.category).HasMaxLength(200);
                entity.Property(e => e.serviceName).HasMaxLength(200);
                // 450 nvarchar ≈ 900 bytes — required for a unique index key on SQL Server
                entity.Property(e => e.key).HasMaxLength(450);
                entity.Property(e => e.value1).HasMaxLength(3990);
                entity.Property(e => e.value2).HasMaxLength(3990);

                // "must have index on this column for performance"
                entity.HasIndex(e => e.serviceID)
                    .HasDatabaseName("IX_CashBackup_serviceID");

                entity.HasIndex(e => e.serviceName)
                    .HasDatabaseName("IX_CashBackup_serviceName");

                // "also must have index on this column for performance"
                entity.HasIndex(e => e.key)
                    .HasDatabaseName("IX_CashBackup_key");

                // "must be unique for each serviceID and category"
                entity.HasIndex(e => new { e.serviceID, e.category, e.key })
                    .IsUnique()
                    .HasDatabaseName("UX_CashBackup_serviceID_category_key");
            });
        }
        #endregion
    }
}

