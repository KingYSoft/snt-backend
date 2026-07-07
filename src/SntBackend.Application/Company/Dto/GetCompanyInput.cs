using System.Collections.Generic;

namespace SntBackend.Application.Company.Dto
{
    /// <summary>
    /// 查询汇率入参（对齐 first-cargo CompanyController.Get 的 GetCompanyInput）。
    /// </summary>
    public class GetCompanyInput
    {
        /// <summary>公司代码（预留，snt 汇率按公司本位币换算，暂不作过滤）。</summary>
        public string CompanyCode { get; set; }

        /// <summary>
        /// 需要换算的外币列表（对应 ZZRefExchangeRate.re_rx_nkexcurrency，即 exrate_from）。
        /// </summary>
        public List<string> HomeCurrency { get; set; }

        /// <summary>发票日期（yyyy-MM-dd），落在 re_startdate ~ re_expirydate 之间。</summary>
        public string InvoiceDate { get; set; }

        /// <summary>
        /// 目标币种（exrate_to）。为空时取公司本位币（第一家启用公司的 gc_rx_nklocalcurrency）。
        /// </summary>
        public string InvoiceCurrency { get; set; }
    }
}
