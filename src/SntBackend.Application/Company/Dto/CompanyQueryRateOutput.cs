namespace SntBackend.Application.Company.Dto
{
    /// <summary>
    /// 汇率查询结果（字段名与 first-cargo 保持一致，前端可直接复用）。
    /// </summary>
    public class CompanyQueryRateOutput
    {
        /// <summary>源币种（外币，ZZRefExchangeRate.re_rx_nkexcurrency）。</summary>
        public string exrate_from { get; set; }

        /// <summary>目标币种（公司本位币或 InvoiceCurrency）。</summary>
        public string exrate_to { get; set; }

        /// <summary>卖出汇率（ZZRefExchangeRate.re_sellrate）；同币种为 "1"。</summary>
        public string exrate_sell_rate { get; set; }
    }
}
