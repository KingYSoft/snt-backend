using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    /// <summary>
    /// 新增 / 修改 应收应付费用（JobCharge）入参。
    /// 字段沿用 QueryChargeLine 的投影命名，前端可直接回传。
    /// </summary>
    public class BillingCreateInput
    {
        /// <summary>
        /// JobShipment.js_pk
        /// </summary>
        public string shpPk { get; set; }

        public List<BillingChargeWriteItem> charges { get; set; } = new();
    }

    /// <summary>
    /// 单条费用行。snt 的 JobCharge 是 BTH（一行同时含 AR/AP 两侧），
    /// 这里每条 item 只描述 chargeType 指定的那一侧。
    /// </summary>
    public class BillingChargeWriteItem
    {
        /// <summary>JobCharge.jr_pk；为空表示新增，有值表示修改</summary>
        public string jr_pk { get; set; }

        /// <summary>AR 取销售侧，AP 取成本侧</summary>
        public string chargeType { get; set; }

        /// <summary>业务费用代码 jr_chargetype</summary>
        public string jr_chargetype { get; set; }

        /// <summary>jr_desc</summary>
        public string jr_desc { get; set; }

        /// <summary>本位币金额（AR=jr_localsellamt / AP=jr_localcostamt）</summary>
        public decimal? amount { get; set; }

        /// <summary>原币金额（AR=jr_ossellamt / AP=jr_oscostamt）</summary>
        public decimal? os_amount { get; set; }

        /// <summary>币种（AR=jr_rx_nksellcurrency / AP=jr_rx_nkcostcurrency）</summary>
        public string currency { get; set; }

        /// <summary>客户/供应商（AR=jr_oh_sellaccount / AP=jr_oh_costaccount）</summary>
        public string party_oh { get; set; }

        /// <summary>汇率（AR=jr_ossellexrate / AP=jr_oscostexrate）</summary>
        public decimal? exchange_rate { get; set; }

        /// <summary>GST 税率代码（AR=jr_at_sellgstrate / AP=jr_at_costgstrate）</summary>
        public string gst_rate { get; set; }

        /// <summary>WHT 税率代码（AR=jr_aw_sellwhtrate / AP=jr_aw_costwhtrate）</summary>
        public string wht_rate { get; set; }

        /// <summary>VAT class（AR=jr_a9_sellvatclass / AP=jr_a9_costvatclass）</summary>
        public string vat_class { get; set; }
    }
}
