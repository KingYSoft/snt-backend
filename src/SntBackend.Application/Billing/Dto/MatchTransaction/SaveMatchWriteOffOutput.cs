using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class SaveMatchWriteOffOutput
    {
        public string MatchNumber { get; set; }
        public List<string> TransactionHeaderPks { get; set; } = new();
        public int AffectedInvoiceCount { get; set; }
        public decimal TotalWriteOffAmountOriginal { get; set; }
        public decimal TotalWriteOffAmountHome { get; set; }
    }
}
