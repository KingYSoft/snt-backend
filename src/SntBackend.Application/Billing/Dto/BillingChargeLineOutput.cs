using System;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class BillingChargeLineOutput
    {
        public int TotalCount { get; set; }

        public List<BillingChargeLineItem> Items { get; set; } = new();
    }

    public class BillingChargeLineItem
    {
        public string jr_pk { get; set; }
        public string jr_jh { get; set; }
        /// <summary>费用分类码 jr_chargetype（'MRG' 这类，非费用代码）</summary>
        public string jr_chargetype { get; set; }
        /// <summary>费用代码 pk jr_ac（AccChargeCode.ac_pk），前端回选下拉用</summary>
        public string jr_ac { get; set; }
        /// <summary>费用代码 AccChargeCode.ac_code（显示用）</summary>
        public string charge_code { get; set; }
        /// <summary>费用代码描述 AccChargeCode.ac_desc（显示用）</summary>
        public string charge_desc { get; set; }
        public string jr_desc { get; set; }
        public decimal? amount { get; set; }
        public decimal? os_amount { get; set; }
        /// <summary>
        /// 数量。来源见 <see cref="ChargeRatingSql"/>：优先已开票行 al_unitqty，
        /// 否则取计价依据 SUM(JobPaymentBasis.PBS_ChargeableAmount)。
        /// 两边都没有时为 null —— 实测只有 19% 的费用行挂了计价依据。
        /// </summary>
        public decimal? qty { get; set; }

        /// <summary>单价。优先已开票行 al_unitprice，否则取 JobPaymentBasis.PBS_PerUnitRate。多档费率时为 null。</summary>
        public decimal? unit_price { get; set; }

        /// <summary>
        /// 计价单位 JobPaymentBasis.PBS_ChargeableUnit，实测取值是箱型（如 '40HC'、'CN'）。
        /// 多档费率时为 null，见 <see cref="rating_line_count"/>。
        /// </summary>
        public string unit { get; set; }

        /// <summary>
        /// 该费用行同一侧（AR/AP）的计价依据条数。1 = 单一费率；&gt;1 = 混装多档
        /// （此时 <see cref="qty"/> 是合计，<see cref="unit"/> 与 <see cref="unit_price"/> 置空，
        /// 前端应提示"多档费率"）；0 或 null = 该行没有计价依据。
        /// </summary>
        public int? rating_line_count { get; set; }
        public string currency { get; set; }
        public string party_oh { get; set; }
        /// <summary>客户/供应商代码 OrgHeader.oh_code</summary>
        public string party_code { get; set; }
        /// <summary>客户/供应商名称 OrgHeader.oh_fullname</summary>
        public string party_name { get; set; }
        public decimal? exchange_rate { get; set; }
        public string gst_rate { get; set; }
        public string wht_rate { get; set; }
        public string vat_class { get; set; }
        /// <summary>发票类型 jr_invoicetype</summary>
        public string jr_invoicetype { get; set; }
        /// <summary>分公司/分支 jr_gb（GlbBranch.gb_pk）</summary>
        public string jr_gb { get; set; }
        /// <summary>分支代码 GlbBranch.gb_code</summary>
        public string branch_code { get; set; }
        /// <summary>分支名称 GlbBranch.gb_branchname</summary>
        public string branch_name { get; set; }
        public string line_pk { get; set; }
        public string invoice_pk { get; set; }
        public string invoice_no { get; set; }
        public DateTime? invoice_date { get; set; }
        public string Draft { get; set; }
    }
}
