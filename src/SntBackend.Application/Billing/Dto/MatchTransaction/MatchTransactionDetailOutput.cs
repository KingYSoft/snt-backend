using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class MatchTransactionDetailOutput
    {
        public AccTransactionHeaderDtoOutput Header { get; set; }
        public AccBankAccountDtoOutput Bank { get; set; }
        public List<OutstandingInvoiceItem> Lines { get; set; } = new();
    }
}
