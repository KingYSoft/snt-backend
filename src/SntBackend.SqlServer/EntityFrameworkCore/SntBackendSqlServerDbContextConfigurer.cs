using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace SntBackend.SqlServer.EntityFrameworkCore
{
    public class SntBackendSqlServerDbContextConfigurer
    {
        public static void Configure(DbContextOptionsBuilder<SntBackendSqlServerDbContext> builder, string connectionString)
        {
            builder.UseSqlServer(connectionString);
        }

        public static void Configure(DbContextOptionsBuilder<SntBackendSqlServerDbContext> builder, DbConnection connection)
        {
            builder.UseSqlServer(connection);
        }
    }
}
