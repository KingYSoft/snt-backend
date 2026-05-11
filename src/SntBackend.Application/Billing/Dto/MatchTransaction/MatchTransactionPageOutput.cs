using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class MatchTransactionPageOutput
    {
        public int TotalCount { get; set; }
        public List<MatchTransactionPageItem> Items { get; set; } = new();
    }

    public class MatchTransactionPageItem
    {
        public string Pk { get; set; }
        public string Ledger { get; set; }
        public string MatchNumber { get; set; }
        public string BillingParty { get; set; }
        public string BillingPartyName { get; set; }
        public string Currency { get; set; }
        public decimal SettledAmount { get; set; }
        public string PaymentDate { get; set; }
        public string Description { get; set; }
    }
}
