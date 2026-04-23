using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class BillingTblInput
    {
        public int SkipCount { get; set; }
        public int MaxResultCount { get; set; } = 20;
        public List<BillingTblFilterItem> filters { get; set; } = new();
    }

    public class BillingTblFilterItem
    {
        public string key { get; set; }
        public string op { get; set; }
        public string val { get; set; }
        public string start { get; set; }
        public string end { get; set; }
    }
}
