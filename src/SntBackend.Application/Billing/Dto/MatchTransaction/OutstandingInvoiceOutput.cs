using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class OutstandingInvoiceOutput
    {
        public int TotalCount { get; set; }
        public List<OutstandingInvoiceItem> Items { get; set; } = new();
    }

    public class OutstandingInvoiceItem
    {
        public string Id { get; set; }
        public string TthPk { get; set; }
        public string Ledger { get; set; }
        public string JobNo { get; set; }
        public string TaxInvoiceNo { get; set; }
        public string InvoiceNumber { get; set; }
        public System.DateTime? BillingDate { get; set; }
        public string ChargeDesc { get; set; }
        public decimal? Outstanding { get; set; }
        public decimal? SettlementAmountOriginal { get; set; }
        public decimal? ExRate { get; set; }
        public decimal? SettlementAmountHome { get; set; }
        public string Currency { get; set; }
    }
}
