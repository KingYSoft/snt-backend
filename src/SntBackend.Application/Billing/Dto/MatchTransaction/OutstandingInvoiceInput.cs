namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class OutstandingInvoiceInput
    {
        public string BillingParty { get; set; }
        public string LedgerScope { get; set; } = "BOTH";
        public string Query { get; set; }
        public string StatementNo { get; set; }
        public string Currency { get; set; }
        public string ChargeDesc { get; set; }
        public int PageIndex { get; set; } = 0;
        public int PageSize { get; set; } = 2000;
    }
}
