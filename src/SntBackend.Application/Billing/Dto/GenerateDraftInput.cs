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
    /// 过账入参：直接过账——选中的 JobCharge pk 列表 + 侧别（AR/AP）。
    /// 后端内部建发票头/行后立即过账，无需前端预先生成草稿。
    /// </summary>
    public class PostChargeInput
    {
        public List<string> pks { get; set; } = new();

        /// <summary>AR / AP</summary>
        public string chargeType { get; set; }
    }
}
