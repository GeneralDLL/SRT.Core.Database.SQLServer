using Microsoft.EntityFrameworkCore;
using SRT.Core.Domain;
using SRT.Core.Database.SQLServer.ErrorLog.Domain;
using SRT.Core.Databases;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SRT.Core.Database.SQLServer.ErrorLog
{
    public class ErrorReportDBContext : DataBase
    {
        public DbSet<ErrorReport> ErrorReports { get; set; }

        #region Constructors
        public ErrorReportDBContext(DbContextOptions<ErrorReportDBContext> options,
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
                    dbName = "ErrorDatabase",
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
            builder.Services.AddDbContext<ErrorReportDBContext>(options =>
                options.UseSqlServer(
                    ConnectionStrings.SRTCore_ErrorSQL.GetConnectionString(),
                    sql => sql.MigrationsAssembly(typeof(ErrorReportDBContext).Assembly.GetName().Name)));
        }

        public override async Task InsertBaseData()
        {
            await Task.CompletedTask;
        }
        #endregion

        #region On Model Creating
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ErrorReport>(entity =>
            {
                entity.ToTable("ErrorReports");
                entity.HasKey(e => e.id);

                entity.Property(e => e.category).HasMaxLength(200);
                entity.Property(e => e.serviceName).HasMaxLength(200);
                entity.Property(e => e.CorrelationId).HasMaxLength(100);
                entity.Property(e => e.Service).HasMaxLength(200);
                entity.Property(e => e.Environment).HasMaxLength(50);
                entity.Property(e => e.Version).HasMaxLength(100);
                // SQL Server column names ignore case, so this cannot share the name of the legacy category column.
                entity.Property(e => e.Category).HasMaxLength(50).HasColumnName("DiagCategory");
                entity.Property(e => e.Operation).HasMaxLength(100);
                entity.Property(e => e.Severity).HasMaxLength(20);
                entity.Property(e => e.Outcome).HasMaxLength(30);
                entity.Property(e => e.ExceptionType).HasMaxLength(200);
                entity.Property(e => e.Target).HasMaxLength(300);

                entity.HasIndex(e => e.ErrorId)
                    .IsUnique()
                    .HasFilter("[ErrorId] IS NOT NULL")
                    .HasDatabaseName("IX_ErrorReports_ErrorId");

                entity.HasIndex(e => new { e.Category, e.date })
                    .HasDatabaseName("IX_ErrorReports_Category_date");

                entity.HasIndex(e => e.CorrelationId)
                    .HasDatabaseName("IX_ErrorReports_CorrelationId");

                entity.HasIndex(e => e.Target)
                    .HasDatabaseName("IX_ErrorReports_Target");

                entity.HasIndex(e => e.serviceName)
                    .HasDatabaseName("IX_ErrorReports_serviceName");

                entity.HasIndex(e => e.category)
                    .HasDatabaseName("IX_ErrorReports_category");

                entity.HasIndex(e => e.serviceID)
                    .HasDatabaseName("IX_ErrorReports_serviceID");
            });
        }
        #endregion
    }
}
