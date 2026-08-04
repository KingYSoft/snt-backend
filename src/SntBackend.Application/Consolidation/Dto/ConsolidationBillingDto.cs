using SntBackend.Application.Billing.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Consolidation.Dto
{
    /// <summary>
    /// 合单费用行分页入参。与 shipment 侧的 <see cref="BillingChargeLineInput"/> 一致，
    /// 只是锚点换成合单 JobConsol.jk_pk。
    /// </summary>
    public class ConsolBillingChargeLineInput
    {
        /// <summary>JobConsol.jk_pk</summary>
        public string jkPk { get; set; }

        /// <summary>AR / AP</summary>
        public string chargeType { get; set; }

        public int SkipCount { get; set; }

        public int MaxResultCount { get; set; } = 20;

        public string Sorting { get; set; }
    }

    /// <summary>
    /// 合单发票头分页入参。
    /// </summary>
    public class ConsolBillingDraftPageInput
    {
        /// <summary>JobConsol.jk_pk</summary>
        public string jkPk { get; set; }

        /// <summary>AR / AP；留空不按账本过滤</summary>
        public string chargeType { get; set; }

        public int SkipCount { get; set; }

        public int MaxResultCount { get; set; } = 20;

        public string Sorting { get; set; }
    }

    /// <summary>
    /// 合单费用新增 / 修改入参。费用行条目直接复用 shipment 侧的 <see cref="BillingChargeWriteItem"/>。
    /// </summary>
    public class ConsolBillingCreateInput
    {
        /// <summary>JobConsol.jk_pk</summary>
        public string jkPk { get; set; }

        public List<BillingChargeWriteItem> charges { get; set; } = new();
    }
}
