using Abp.Configuration.Startup;
using Abp.Localization.Dictionaries;
using Abp.Localization.Dictionaries.Xml;
using Abp.Reflection.Extensions;
using SntBackend.DomainService.Share;

namespace SntBackend.DomainService.Localization
{
    public static class SntBackendLocalizationConfigurer
    {
        public static void Configure(ILocalizationConfiguration localizationConfiguration)
        {
            localizationConfiguration.Sources.Add(
                new DictionaryBasedLocalizationSource(SntBackendConsts.LocalizationSourceName,
                    new XmlEmbeddedFileLocalizationDictionaryProvider(
                        typeof(SntBackendLocalizationConfigurer).GetAssembly(),
                        "SntBackend.DomainService.Localization.SourceFiles"
                    )
                )
            );
        }
    }
}
