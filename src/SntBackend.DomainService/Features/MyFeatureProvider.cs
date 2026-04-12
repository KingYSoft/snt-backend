using Abp.Application.Features;
using Abp.Localization;
using Abp.UI.Inputs;
using SntBackend.DomainService.Share;

namespace SntBackend.DomainService.Features
{
    public class MyFeatureProvider : FeatureProvider
    {
        public override void SetFeatures(IFeatureDefinitionContext context)
        {
            var demo = context.Create(
                FeatureNameConsts.FEATURE_DEMO,
                defaultValue: "true",
                displayName: L("Deature.Demo"),
                inputType: new CheckboxInputType()
            );
        }

        private static ILocalizableString L(string name)
        {
            return new LocalizableString(name, SntBackendConsts.LocalizationSourceName);
        }
    }
}
