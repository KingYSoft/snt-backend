using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class BillingTblOutput
    {
        public int TotalCount { get; set; }
        public List<AccTransactionHeaderDtoOutput> Items { get; set; } = new();
    }
}
