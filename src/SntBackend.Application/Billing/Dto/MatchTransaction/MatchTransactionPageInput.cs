namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    using System.Collections.Generic;
    using SntBackend.Application.Excel;

    public class MatchTransactionPageInput
    {
        public string Shipper { get; set; }
        public string JobNumber { get; set; }
        public string MatchNumber { get; set; }
        public string Description { get; set; }
        public string EtdStart { get; set; }
        public string EtdEnd { get; set; }
        public string PaymentDateStart { get; set; }
        public string PaymentDateEnd { get; set; }
        public int SkipCount { get; set; }
        public int MaxResultCount { get; set; } = 20;
        public List<string> SelectedPks { get; set; } = new();
        public List<ExportColumnInput> Columns { get; set; } = new();
        public string ExportTitle { get; set; }
        public string CompanyNameLabel { get; set; }
        public string ReportPeriodLabel { get; set; }
    }
}
