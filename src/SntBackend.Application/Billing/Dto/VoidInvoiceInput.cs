using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    /// <summary>
    /// 作废草稿发票入参：草稿发票头 ah_pk 列表（ah_postdate IS NULL）。
    /// </summary>
    public class VoidInvoiceInput
    {
        public List<string> ahPks { get; set; } = new();
    }

    /// <summary>
    /// 编辑草稿发票入参：在某张草稿发票（ahPk）上 删除 / 修改 / 新增 费用。
    /// 侧别由发票头 ah_ledger 决定，charges 里的 chargeType 可不填。
    /// </summary>
    public class DraftInvoiceEditInput
    {
        /// <summary>草稿发票头 ah_pk（必须未过账）</summary>
        public string ahPk { get; set; }

        /// <summary>要从该发票移除的 JobCharge jr_pk 列表</summary>
        public List<string> deleteJrPks { get; set; } = new();

        /// <summary>新增(jr_pk 空) / 修改(jr_pk 有) 的费用</summary>
        public List<BillingChargeWriteItem> charges { get; set; } = new();
    }
}
