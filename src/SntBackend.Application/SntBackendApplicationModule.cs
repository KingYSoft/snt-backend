using Abp.AutoMapper;
using Abp.Modules;
using Abp.Reflection.Extensions;
using SntBackend.DomainService;

namespace SntBackend.Application
{
    [DependsOn(
        typeof(SntBackendDomainServiceModule)
           )]
    public class SntBackendApplicationModule : AbpModule
    {

        public SntBackendApplicationModule()
        {
        }
        public override void PreInitialize()
        {
        }

        public override void Initialize()
        {
            var thisAssembly = typeof(SntBackendApplicationModule).GetAssembly();
            IocManager.RegisterAssemblyByConvention(thisAssembly);
            Configuration.Modules.AbpAutoMapper().Configurators.Add(
                // Scan the assembly for classes which inherit from AutoMapper.Profile
                cfg => cfg.AddMaps(thisAssembly)
            );
        }

    }
}
