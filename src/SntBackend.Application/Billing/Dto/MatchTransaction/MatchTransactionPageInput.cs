namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class MatchTransactionPageInput
    {
        public string Shipper { get; set; }
        public string JobNumber { get; set; }
        public string MatchNumber { get; set; }
        public int SkipCount { get; set; }
        public int MaxResultCount { get; set; } = 20;
    }
}
