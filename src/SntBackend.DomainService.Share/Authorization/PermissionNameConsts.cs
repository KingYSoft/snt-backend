namespace SntBackend.DomainService.Share.Authorization
{
    public static class PermissionNameConsts
    {
        public const string System = "system";
        public const string SystemCompany = "system.company";
        public const string SystemBranch = "system.branch";
        public const string SystemUser = "system.user";
        public const string SystemGroup = "system.group";

        public const string Business = "business";
        public const string BusinessShipment = "business.shipment";
        public const string BusinessShipmentBilling = "business.shipment.billing";
        public const string BusinessShipmentBillingChargeLine = "business.shipment.billing.charge-line";
        public const string BusinessShipmentBillingInvoice = "business.shipment.billing.invoice";
        public const string Settlement = "settlement";
        public const string SettlementReceivable = "settlement.receivable";
        public const string SettlementPayable = "settlement.payable";
    }
}
