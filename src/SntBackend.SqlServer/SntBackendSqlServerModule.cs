using Abp.EntityFrameworkCore.Configuration;
using Abp.Modules;
using Abp.Reflection.Extensions;
using SntBackend.DomainService.Share;
using SntBackend.SqlServer.EntityFrameworkCore;

namespace SntBackend.SqlServer
{
    [DependsOn(
           typeof(SntBackendDomainServiceShareModule)
           )]
    public class SntBackendSqlServerModule : AbpModule
    {
        public SntBackendSqlServerModule()
        {
        }
        public override void PreInitialize()
        {
            Configuration.Modules.AbpEfCore().AddDbContext<SntBackendSqlServerDbContext>(options =>
            {
                if (options.ExistingConnection != null)
                {
                    SntBackendSqlServerDbContextConfigurer.Configure(options.DbContextOptions, options.ExistingConnection);
                }
                else
                {
                    SntBackendSqlServerDbContextConfigurer.Configure(options.DbContextOptions, options.ConnectionString);
                }
            });
        }

        public override void Initialize()
        {
            IocManager.RegisterAssemblyByConvention(typeof(SntBackendSqlServerModule).GetAssembly());
        }

    }
}
