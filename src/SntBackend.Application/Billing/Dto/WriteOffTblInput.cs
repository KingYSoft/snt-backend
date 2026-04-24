using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class WriteOffTblInput
    {
        public int SkipCount { get; set; }
        public int MaxResultCount { get; set; } = 20;
        public List<BillingTblFilterItem> filters { get; set; } = new();
    }
}
