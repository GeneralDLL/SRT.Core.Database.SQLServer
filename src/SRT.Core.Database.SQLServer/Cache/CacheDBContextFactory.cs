using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SRT.Core.Domain;

namespace SRT.Core.Database.SQLServer.Cache
{
    /// <summary>
    /// Design-time factory used only by EF tools (<c>dotnet ef migrations add/remove</c>).
    /// Not used at runtime — the host registers <see cref="CacheDBContext"/> via DI.
    /// Required because <see cref="CacheDBContext"/> needs constructor injection and has no parameterless ctor,
    /// so EF cannot create it without this factory.
    /// </summary>
    public sealed class CacheDBContextFactory : IDesignTimeDbContextFactory<CacheDBContext>
    {
        public CacheDBContext CreateDbContext(string[] args)
        {
            var connection = new GeneralConnectionString
            {
                connectionType = "SQL",
                dbName = "CacheDatabase",
                host = "localhost",
                port = "11433",
                user = "sa",
                pass = "SaeedTNT220"
            };

            var options = new DbContextOptionsBuilder<CacheDBContext>()
                .UseSqlServer(
                    connection.GetConnectionString(),
                    sql => sql.MigrationsAssembly(typeof(CacheDBContext).Assembly.GetName().Name))
                .Options;

            return new CacheDBContext(options, new AppConnectionString { SRTCore_CacheSQL = connection });
        }
    }
}
