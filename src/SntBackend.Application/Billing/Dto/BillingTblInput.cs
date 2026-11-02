using System.Collections.Generic;
using SntBackend.Application.Excel;

namespace SntBackend.Application.Billing.Dto
{
    public class BillingTblInput
    {
        public int SkipCount { get; set; }
        public int MaxResultCount { get; set; } = 20;
        public List<BillingTblFilterItem> filters { get; set; } = new();
        public string job_number { get; set; }
        public string job_invoice_number { get; set; }
        public List<ExportColumnInput> columns { get; set; } = new();
        public string exportTitle { get; set; }
        public string companyNameLabel { get; set; }
        public string reportPeriodLabel { get; set; }
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
