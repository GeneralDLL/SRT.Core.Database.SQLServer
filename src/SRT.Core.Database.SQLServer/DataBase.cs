using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using SRT.Core.Domain;
using SRT.Core.Interfaces.Databases;

namespace SRT.Core.Databases
{
    public abstract class DataBase : DbContext, IDataBase
    {
        public AppConnectionString ConnectionStrings { get; }

        public virtual DatabaseProvider Provider => DatabaseProvider.SqlServer;

        /// <summary>
        /// When true, startup runs identity-counter maintenance (upward reseed only).
        /// Default false; domain contexts that need it must override to true.
        /// Cache/ErrorLog contexts leave this false.
        /// </summary>
        public virtual bool RunIdentityGapFix => false;

        public DataBase(DbContextOptions options, AppConnectionString connectionStrings) : base(options)
        {
            ConnectionStrings = connectionStrings;
        }

        public abstract void BaseConfiguration(WebApplicationBuilder builder, AppConnectionString ConnectionStrings);

        public abstract Task InsertBaseData();

        public virtual Task ApplyStartupDataMaintenanceAsync() => Task.CompletedTask;
    }
}
