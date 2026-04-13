using Abp.EntityFrameworkCore.Configuration;
using Abp.Localization;
using Abp.Modules;
using Abp.Reflection.Extensions;
using Facade.Dapper.SqlServer;
using Facade.NLogger;

namespace SntBackend.EntityGenerate
{
    [DependsOn(typeof(FacadeDapperSqlServerModule),
                 typeof(FacadeNLoggerModule))]
    public class SntBackendEntityGenerateModule : AbpModule
    {
        public override void PreInitialize()
        {

            Configuration.Auditing.IsEnabled = false;
            Configuration.Auditing.IsEnabledForAnonymousUsers = true;
            Configuration.BackgroundJobs.IsJobExecutionEnabled = false;
            Configuration.MultiTenancy.IsEnabled = false;
            Configuration.DefaultNameOrConnectionString = Consts.DefaultNameOrConnectionString;

            Configuration.Localization.Languages.Clear();
            Configuration.Localization.Languages.Add(new LanguageInfo("en", "English", isDefault: true));
            Configuration.Localization.Languages.Add(new LanguageInfo("zh-Hans", "中文简体"));
            // set licence
            Facade.Configuration.FacadeCoreConfigurationExtensions.Facade(Configuration.Modules).Configure(options =>
            {
                options.Licence = "fc_vC4Fod/3q1pRA8AJuAKONYC5n0ctI15tOOk6KMMSIet5dr8JWvIZH5hmLhVw1AEg";
            });
        }

        public override void Initialize()
        {
            IocManager.RegisterAssemblyByConvention(typeof(SntBackendEntityGenerateModule).GetAssembly());
        }
    }
}
