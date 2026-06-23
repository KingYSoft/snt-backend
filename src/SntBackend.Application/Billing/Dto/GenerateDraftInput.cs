using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    /// <summary>
    /// 生成草稿入参：选中的 JobCharge pk 列表 + 侧别。
    /// snt 的 JobCharge 是 BTH，需要 chargeType 指定对哪一侧生成草稿发票。
    /// </summary>
    public class GenerateDraftInput
    {
        public List<string> pks { get; set; } = new();

        /// <summary>AR / AP</summary>
        public string chargeType { get; set; }
    }

    /// <summary>
    /// 过账入参：草稿发票头 ah_pk 列表（ah_postdate IS NULL 的草稿）。
    /// </summary>
    public class PostChargeInput
    {
        public List<string> ahPks { get; set; } = new();
    }
}
