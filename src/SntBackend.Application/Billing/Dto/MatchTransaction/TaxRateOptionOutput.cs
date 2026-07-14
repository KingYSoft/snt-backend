namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    /// <summary>GST 税率下拉项（来源 AccTaxRate）。前端选择后回传 pk。</summary>
    public class GstRateOptionOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string desc { get; set; }
    }

    /// <summary>WHT 预扣税下拉项（来源 AccWithholding）。前端选择后回传 pk。</summary>
    public class WhtRateOptionOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string desc { get; set; }
        /// <summary>税率 AW_Rate</summary>
        public decimal? rate { get; set; }
    }

    /// <summary>VAT class 下拉项（来源 AccInvMsg）。前端选择后回传 pk。</summary>
    public class VatClassOptionOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string desc { get; set; }
    }
}
