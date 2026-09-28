using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using SRT.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SRT.Core.Databases
{
    public interface IDataBase
    {
        void BaseConfiguration(WebApplicationBuilder builder, AppConnectionString ConnectionStrings);

        Task InsertBaseData();

        /// <summary>همگام‌سازی دادهٔ وابسته به قوانین کسب‌وکار (جدا از seed) — هر استارتاپ.</summary>
        Task ApplyStartupDataMaintenanceAsync();
    }

    public abstract class DataBase : DbContext, IDataBase
    {
        public AppConnectionString ConnectionStrings { get; }

        public DataBase(DbContextOptions options, AppConnectionString connectionStrings) : base(options)
        {
            ConnectionStrings = connectionStrings;
        }

        public abstract void BaseConfiguration(WebApplicationBuilder builder, AppConnectionString ConnectionStrings);

        public abstract Task InsertBaseData();

        public virtual Task ApplyStartupDataMaintenanceAsync() => Task.CompletedTask;
    }
}
