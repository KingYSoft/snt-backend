using Abp.Application.Features;
using Abp.Application.Navigation;
using Abp.Authorization;
using Abp.Configuration;
using Abp.Configuration.Startup;
using Abp.Dependency;
using Abp.Localization;
using Abp.Runtime.Session;
using Abp.Timing;
using Abp.Timing.Timezone;
using Abp.Web.Configuration;
using Abp.Web.Models.AbpUserConfiguration;
using Abp.Web.Security.AntiForgery;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Configuration
{
    public class MyUserConfigurationBuilder : AbpUserConfigurationBuilder, ITransientDependency
    {
        private readonly IIocResolver _iocResolver;

        public MyUserConfigurationBuilder(IMultiTenancyConfig multiTenancyConfig,
            ILanguageManager languageManager,
            ILocalizationManager localizationManager,
            IFeatureManager featureManager,
            IFeatureChecker featureChecker,
            IPermissionManager permissionManager,
            IUserNavigationManager userNavigationManager,
            ISettingDefinitionManager settingDefinitionManager,
            ISettingManager settingManager,
            IAbpAntiForgeryConfiguration abpAntiForgeryConfiguration,
            IAbpSession abpSession, IPermissionChecker permissionChecker,
            IIocResolver iocResolver, IAbpStartupConfiguration startupConfiguration)
            : base(multiTenancyConfig,
                  languageManager,
                  localizationManager,
                  featureManager,
                  featureChecker,
                  permissionManager,
                  userNavigationManager,
                  settingDefinitionManager,
                  settingManager,
                  abpAntiForgeryConfiguration,
                  abpSession,
                  permissionChecker,
                  iocResolver,
                  startupConfiguration)
        {
            _iocResolver = iocResolver;
        }

        public override async Task<AbpUserConfigurationDto> GetAll()
        {
            var dto = await base.GetAll();
            dto.Nav = await GetUserNavConfig();
            dto.Auth = await GetUserAuthConfig();
            dto.Setting = await GetUserSettingConfig();
            dto.Localization = GetUserLocalizationConfig();
            dto.Features = await GetUserFeaturesConfig();
            return dto;
        }
        protected override async Task<AbpUserAuthConfigDto> GetUserAuthConfig()
        {
            var config = new AbpUserAuthConfigDto();
            config.AllPermissions = new Dictionary<string, string>();
            config.GrantedPermissions = new Dictionary<string, string>();

            if (AbpSession.UserId.HasValue)
            {
                var allPermissionNames = PermissionManager.GetAllPermissions()
                    //.Where(a => a.CheckUserType(userType))
                    .Select(p => p.Name)
                    .ToList();
                var grantedPermissionNames = new List<string>();

                foreach (var permissionName in allPermissionNames)
                {
                    if (await PermissionChecker.IsGrantedAsync(permissionName))
                    {
                        grantedPermissionNames.Add(permissionName);
                    }
                }

                // config.AllPermissions = allPermissionNames.ToDictionary(permissionName => permissionName, permissionName => "true");
                config.GrantedPermissions = grantedPermissionNames.ToDictionary(permissionName => permissionName, permissionName => "true");
            }
            return config;
        }

        protected override async Task<AbpUserNavConfigDto> GetUserNavConfig()
        {
            var userMenus = await UserNavigationManager.GetMenusAsync(AbpSession.ToUserIdentifier());
            return new AbpUserNavConfigDto
            {
                Menus = userMenus.ToDictionary(userMenu => userMenu.Name, userMenu => userMenu)
            };
        }
        protected override async Task<AbpUserSettingConfigDto> GetUserSettingConfig()
        {
            var config = new AbpUserSettingConfigDto
            {
                Values = new Dictionary<string, string>()
            };

            var settings = await SettingManager.GetAllSettingValuesAsync(SettingScopes.All);
            using (var scope = _iocResolver.CreateScope())
            {
                foreach (var settingValue in settings)
                {
                    if (!await SettingDefinitionManager.GetSettingDefinition(settingValue.Name).ClientVisibilityProvider
                        .CheckVisible(scope))
                    {
                        continue;
                    }

                    config.Values.Add(settingValue.Name, settingValue.Value);
                }
            }

            return config;
        }

        protected override AbpUserLocalizationConfigDto GetUserLocalizationConfig()
        {
            var currentCulture = CultureInfo.CurrentUICulture;
            var languages = LanguageManager.GetActiveLanguages();

            var config = new AbpUserLocalizationConfigDto
            {
                CurrentCulture = new AbpUserCurrentCultureConfigDto
                {
                    Name = currentCulture.Name,
                    DisplayName = currentCulture.DisplayName
                },
                Languages = languages.ToList()
            };

            if (languages.Count > 0)
            {
                config.CurrentLanguage = LanguageManager.CurrentLanguage;
            }

            var sources = LocalizationManager.GetAllSources().OrderBy(s => s.Name).ToArray();
            config.Sources = sources.Select(s => new AbpLocalizationSourceDto
            {
                Name = s.Name,
                Type = s.GetType().Name
            }).ToList();

            config.Values = new Dictionary<string, Dictionary<string, string>>();
            foreach (var source in sources)
            {
                var stringValues = source.GetAllStrings(currentCulture).OrderBy(s => s.Name).ToList();
                var stringDictionary = stringValues
                    .ToDictionary(_ => _.Name, _ => _.Value);
                config.Values.Add(source.Name, stringDictionary);
            }

            return config;
        }
        protected override async Task<AbpUserFeatureConfigDto> GetUserFeaturesConfig()
        {
            var config = new AbpUserFeatureConfigDto()
            {
                AllFeatures = new Dictionary<string, AbpStringValueDto>()
            };

            var allFeatures = FeatureManager.GetAll().ToList();

            if (AbpSession.TenantId.HasValue)
            {
                var currentTenantId = AbpSession.GetTenantId();
                foreach (var feature in allFeatures)
                {
                    var value = await FeatureChecker.GetValueAsync(currentTenantId, feature.Name);
                    config.AllFeatures.Add(feature.Name, new AbpStringValueDto
                    {
                        Value = value
                    });
                }
            }
            else
            {
                foreach (var feature in allFeatures)
                {
                    config.AllFeatures.Add(feature.Name, new AbpStringValueDto
                    {
                        Value = feature.DefaultValue
                    });
                }
            }

            return config;
        }

        protected override async Task<AbpUserTimingConfigDto> GetUserTimingConfig()
        {
            var timezoneId = await SettingManager.GetSettingValueAsync(TimingSettingNames.TimeZone);
            if (timezoneId != "UTC")
            {
                var timezone = TimezoneHelper.FindTimeZoneInfo(timezoneId);

                return new AbpUserTimingConfigDto
                {
                    TimeZoneInfo = new AbpUserTimeZoneConfigDto
                    {
                        Windows = new AbpUserWindowsTimeZoneConfigDto
                        {
                            TimeZoneId = TimezoneHelper.IanaToWindows(timezoneId), //timezoneId,
                            BaseUtcOffsetInMilliseconds = timezone.BaseUtcOffset.TotalMilliseconds,
                            CurrentUtcOffsetInMilliseconds = timezone.GetUtcOffset(Clock.Now).TotalMilliseconds,
                            IsDaylightSavingTimeNow = timezone.IsDaylightSavingTime(Clock.Now)
                        },
                        Iana = new AbpUserIanaTimeZoneConfigDto
                        {
                            TimeZoneId = timezoneId //TimezoneHelper.WindowsToIana(timezoneId)
                        }
                    }
                };
            }
            else
            {
                return await base.GetUserTimingConfig();
            }
        }
    }
}
