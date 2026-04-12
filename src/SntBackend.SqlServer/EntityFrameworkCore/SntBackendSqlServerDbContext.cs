using Abp.EntityFrameworkCore;
using Facade.Dapper.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace SntBackend.SqlServer.EntityFrameworkCore
{
    public class SntBackendSqlServerDbContext : AbpDbContext
    {
        // 配置 DbSet 自动注册 ef core IRepotory 


        public SntBackendSqlServerDbContext(DbContextOptions<SntBackendSqlServerDbContext> options)
          : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // table 配置

        }
    }
}
