using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SRT.Core.Domain;

namespace SRT.Core.Database.SQLServer.ErrorLog
{
    /// <summary>
    /// Design-time factory used only by EF tools (<c>dotnet ef migrations add/remove</c>).
    /// Not used at runtime — the host registers <see cref="ErrorReportDBContext"/> via DI.
    /// Required because <see cref="ErrorReportDBContext"/> needs constructor injection and has no parameterless ctor.
    /// </summary>
    public sealed class ErrorReportDBContextFactory : IDesignTimeDbContextFactory<ErrorReportDBContext>
    {
        public ErrorReportDBContext CreateDbContext(string[] args)
        {
            var connection = new GeneralConnectionString
            {
                connectionType = "SQL",
                dbName = "ErrorDatabase",
                host = "localhost",
                port = "11433",
                user = "sa",
                pass = "SaeedTNT220"
            };

            var options = new DbContextOptionsBuilder<ErrorReportDBContext>()
                .UseSqlServer(
                    connection.GetConnectionString(),
                    sql => sql.MigrationsAssembly(typeof(ErrorReportDBContext).Assembly.GetName().Name))
                .Options;

            return new ErrorReportDBContext(options, new AppConnectionString { SRTCore_ErrorSQL = connection });
        }
    }
}
