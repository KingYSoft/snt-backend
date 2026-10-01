using Abp.Authorization;
using Abp.Localization;
using SntBackend.DomainService.Share;
using SntBackend.DomainService.Share.Authorization;

namespace SntBackend.DomainService.Authorization
{
    public class MyAuthorizationProvider : AuthorizationProvider
    {
        public override void SetPermissions(IPermissionDefinitionContext context)
        {
            var root = context.CreatePermission(PermissionNameConsts.System, L("Permission.System"));
            root.CreateChildPermission(PermissionNameConsts.SystemCompany, L("Permission.System.Company"));
            root.CreateChildPermission(PermissionNameConsts.SystemBranch, L("Permission.System.Branch"));
            root.CreateChildPermission(PermissionNameConsts.SystemUser, L("Permission.System.User"));
            root.CreateChildPermission(PermissionNameConsts.SystemGroup, L("Permission.System.Group"));

            var business = context.CreatePermission(PermissionNameConsts.Business, L("Permission.Business"));
            var shipment = business.CreateChildPermission(PermissionNameConsts.BusinessShipment, L("Permission.Business.Shipment"));
            var billing = shipment.CreateChildPermission(PermissionNameConsts.BusinessShipmentBilling, L("Permission.Business.Shipment.Billing"));
            billing.CreateChildPermission(PermissionNameConsts.BusinessShipmentBillingChargeLine, L("Permission.Business.Shipment.Billing.ChargeLine"));
            billing.CreateChildPermission(PermissionNameConsts.BusinessShipmentBillingInvoice, L("Permission.Business.Shipment.Billing.Invoice"));

            var settlement = context.CreatePermission(PermissionNameConsts.Settlement, L("Permission.Settlement"));
            settlement.CreateChildPermission(PermissionNameConsts.SettlementReceivable, L("Permission.Settlement.Receivable"));
            settlement.CreateChildPermission(PermissionNameConsts.SettlementPayable, L("Permission.Settlement.Payable"));
        }

        private static LocalizableString L(string name)
        {
            return new LocalizableString(name, SntBackendConsts.LocalizationSourceName);
        }
    }
}
