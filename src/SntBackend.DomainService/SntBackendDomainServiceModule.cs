using Abp.AutoMapper;
using Abp.Configuration.Startup;
using Abp.Dependency;
using Abp.Domain.Uow;
using Abp.MailKit;
using Abp.Modules;
using Abp.Reflection.Extensions;
using Abp.Threading.BackgroundWorkers;
using Facade.AutoMapper;
using Facade.Quartz;
using SntBackend.DomainService.BackgroundWorkers;
using SntBackend.DomainService.Features;
using SntBackend.DomainService.Localization;
using SntBackend.DomainService.Navigation;
using SntBackend.DomainService.SettingDefinitions;
using SntBackend.DomainService.Share;

using SntBackend.SqlServer;

namespace SntBackend.DomainService
{
    [DependsOn( 
        typeof(SntBackendSqlServerModule), 
        typeof(FacadeQuartzModule),
        typeof(FacadeAutoMapperModule)
           )]
    public class SntBackendDomainServiceModule : AbpModule
    {

        public SntBackendDomainServiceModule()
        {
        }
        public override void PreInitialize()
        {
            Configuration.ReplaceService<IConnectionStringResolver, MyConnectionStringResolver>();

            SntBackendLocalizationConfigurer.Configure(Configuration.Localization);

            //Configuration.Authorization.Providers.Add<MyAuthorizationProvider>();
            Configuration.Navigation.Providers.Add<MyNavigationProvider>();
            Configuration.Features.Providers.Add<MyFeatureProvider>();

            Configuration.Settings.Providers.Add<MyEmailSettingProvider>();
            Configuration.Settings.Providers.Add<MyLocalizationSettingProvider>();

            Configuration.ReplaceService<IMailKitSmtpBuilder, MyMailKitSmtpBuilder>(DependencyLifeStyle.Transient);
        }

        public override void Initialize()
        {
            var thisAssembly = typeof(SntBackendDomainServiceModule).GetAssembly();
            IocManager.RegisterAssemblyByConvention(thisAssembly);
            Configuration.Modules.AbpAutoMapper().Configurators.Add(
                // Scan the assembly for classes which inherit from AutoMapper.Profile
                //cfg => cfg.AddProfiles(thisAssembly)
                cfg => cfg.AddMaps(thisAssembly)
            );
        }

        public override void PostInitialize()
        {
            if (Configuration.BackgroundJobs.IsJobExecutionEnabled)
            {
                //Worker DI.
                IocManager.Resolve<IBackgroundWorkerManager>().Add(IocManager.Resolve<ClearLoggerWorker>());
            }
        }
    }
}
