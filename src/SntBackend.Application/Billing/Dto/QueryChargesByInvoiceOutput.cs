using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class QueryChargesByInvoiceOutput
    {
        public AccTransactionHeaderDtoOutput Head { get; set; }

        public List<AccTransactionLinesDtoOutput> Lines { get; set; } = new();

        public List<BillingChargeLineItem> Charges { get; set; } = new();

        public string BillingParty { get; set; }
    }
}
