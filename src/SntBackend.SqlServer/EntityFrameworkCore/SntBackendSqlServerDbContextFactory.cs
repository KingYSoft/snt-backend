using Facade.Core.Configuration;
using SntBackend.DomainService.Share;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SntBackend.SqlServer.EntityFrameworkCore
{
    public class SntBackendSqlServerDbContextFactory : IDesignTimeDbContextFactory<SntBackendSqlServerDbContext>
    {
        public SntBackendSqlServerDbContext CreateDbContext(string[] args)
        {
            var builder = new DbContextOptionsBuilder<SntBackendSqlServerDbContext>();
            var configuration = ConfigurationHelper.BuildConfiguration(new FacadeConfigurationBuilderOptions()
            {
                BasePath = WebContentDirectoryFinder.CalculateContentRootFolder(),
                EnvironmentName = "Development"
            });
            System.Console.WriteLine(configuration.GetConnectionString(SntBackendConsts.ConnectionStringName));

            SntBackendSqlServerDbContextConfigurer.Configure(builder, configuration.GetConnectionString(SntBackendConsts.ConnectionStringName));

            return new SntBackendSqlServerDbContext(builder.Options);
        }
    }
}
