namespace SntBackend.Application.Billing.Dto
{
    public class BillingDraftPageInput
    {
        public string shpPk { get; set; }

        public string chargeType { get; set; }

        public int SkipCount { get; set; }

        public int MaxResultCount { get; set; } = 20;

        public string Sorting { get; set; }
    }
}
