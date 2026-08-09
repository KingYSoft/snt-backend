using System;
using System.Collections.Generic;

namespace SntBackend.Application.Consolidation.Dto
{
    /// <summary>
    /// 合单 AP 成本行分页结果。主行是合单级的 JobConsolCost，
    /// 每行下挂 <see cref="ConsolBillingCostLineItem.cost_items"/> —— 该成本按运单分摊出来的 JobCharge。
    /// </summary>
    public class ConsolBillingCostLineOutput
    {
        public int TotalCount { get; set; }

        public List<ConsolBillingCostLineItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 主行：合单级 AP 成本行（JobConsolCost，E6_ParentID = jk_pk 且 E6_ParentTableCode = 'JK'）。
    /// 这张表只有成本侧，没有卖价列，所以合单账单只支持 AP。
    /// </summary>
    public class ConsolBillingCostLineItem
    {
        /// <summary>JobConsolCost.E6_PK</summary>
        public string e6_pk { get; set; }

        /// <summary>行序 E6_Sequence</summary>
        public int? sequence { get; set; }

        /// <summary>费用代码 pk E6_AC_ChargeCode（AccChargeCode.ac_pk），前端回选下拉用</summary>
        public string e6_ac { get; set; }

        /// <summary>费用代码 AccChargeCode.ac_code（显示用）</summary>
        public string charge_code { get; set; }

        /// <summary>费用代码描述 AccChargeCode.ac_desc（显示用）</summary>
        public string charge_desc { get; set; }

        /// <summary>
        /// 费用说明 E6_Description。实测该列 100% 为空（JobConsolCost 上 1348 行无一有值），
        /// 展示请用 <see cref="display_description"/>。
        /// </summary>
        public string description { get; set; }

        /// <summary>
        /// 展示用说明：E6_Description 有值时取它，否则回退费用代码描述 AccChargeCode.ac_desc。
        /// </summary>
        public string display_description { get; set; }

        /// <summary>原币币种 E6_RX_NKCurrency</summary>
        public string currency { get; set; }

        /// <summary>原币成本金额 E6_OSCostAmount</summary>
        public decimal? os_cost_amount { get; set; }

        /// <summary>原币税额 E6_OSGSTAmount</summary>
        public decimal? os_gst_amount { get; set; }

        /// <summary>汇率 E6_ExchangeRate</summary>
        public decimal? exchange_rate { get; set; }

        /// <summary>
        /// 数量 = SUM(JobPaymentBasis.PBS_ChargeableAmount)（PBS_E6 = 本行 E6_PK）。
        ///
        /// JobConsolCost 表本身没有数量/单位/单价列，这三个值在计价依据表 JobPaymentBasis 上。
        /// 实测校验：qty × unit_price = E6_OSCostAmount（如 4 × 295 = 1180）。
        /// 覆盖率有限：1348 条成本行里只有 353 条挂了计价依据，其余为 null。
        /// </summary>
        public decimal? qty { get; set; }

        /// <summary>
        /// 计价单位 JobPaymentBasis.PBS_ChargeableUnit，实测取值是箱型（如 '40HC'）。
        /// 一条成本行有多档费率时（混装）为 null，见 <see cref="rating_line_count"/>。
        /// </summary>
        public string unit { get; set; }

        /// <summary>
        /// 单价 JobPaymentBasis.PBS_PerUnitRate。多档费率时为 null，见 <see cref="rating_line_count"/>。
        /// </summary>
        public decimal? unit_price { get; set; }

        /// <summary>
        /// 该成本行的计价依据条数。1 = 单一费率（<see cref="unit"/> / <see cref="unit_price"/> 有值）；
        /// &gt;1 = 混装多档（如 20GP + 40HC 两种费率），此时只有 <see cref="qty"/> 是合计值，
        /// 单位与单价置空，前端应提示"多档费率"而不是硬凑一个数。0 或 null = 没有计价依据。
        /// </summary>
        public int? rating_line_count { get; set; }

        /// <summary>本位币成本金额 E6_LocalCostAmount。子行 cost_items 的金额之和应等于该值。</summary>
        public decimal? local_cost_amount { get; set; }

        /// <summary>供应商 pk E6_OH_Creditor（OrgHeader.oh_pk）</summary>
        public string creditor_oh { get; set; }

        /// <summary>供应商代码 OrgHeader.oh_code</summary>
        public string creditor_code { get; set; }

        /// <summary>供应商名称 OrgHeader.oh_fullname</summary>
        public string creditor_name { get; set; }

        /// <summary>成本参考号 E6_CostReference</summary>
        public string cost_reference { get; set; }

        /// <summary>供应商发票号 E6_InvoiceNum（对方开来的票号，不是本系统生成的发票）</summary>
        public string invoice_num { get; set; }

        /// <summary>供应商发票日期 E6_InvoiceDate</summary>
        public DateTime? invoice_date { get; set; }

        /// <summary>分摊方式 E6_ApportionmentMethod</summary>
        public string apportionment_method { get; set; }

        /// <summary>
        /// 税率分类 pk E6_A9_VATClass。实测该列在 JobConsolCost 上全空，
        /// 税代码请用 <see cref="tax_code"/>。
        /// </summary>
        public string vat_class { get; set; }

        /// <summary>税代码 E6_AT_TaxRate → AccTaxRate.AT_Code（如 EXEMPT）</summary>
        public string tax_code { get; set; }

        /// <summary>税代码描述 AccTaxRate.AT_Description（如 Exempt Rated）</summary>
        public string tax_desc { get; set; }

        /// <summary>
        /// 分公司代码。JobConsolCost 自身没有分公司列（E6_GB_CostTaxBranch 实测全空），
        /// 取所链接 AP 发票头的 ah_gb → GlbBranch.GB_Code。未链接发票时为 null。
        /// </summary>
        public string branch_code { get; set; }

        /// <summary>分公司名称 GlbBranch.GB_BranchName</summary>
        public string branch_name { get; set; }

        /// <summary>部门代码，来源同 <see cref="branch_code"/>：ah_ge → GlbDepartment.GE_Code</summary>
        public string dept_code { get; set; }

        /// <summary>部门描述 GlbDepartment.GE_Desc</summary>
        public string dept_desc { get; set; }

        /// <summary>付款日期 E6_PaymentDate</summary>
        public DateTime? payment_date { get; set; }

        /// <summary>付款方式 E6_PaymentType</summary>
        public string payment_type { get; set; }

        /// <summary>本系统 AP 发票头 pk E6_AH_APInvoice（AccTransactionHeader.ah_pk）</summary>
        public string ap_invoice_pk { get; set; }

        /// <summary>AP 发票号 AccTransactionHeader.ah_transactionnum</summary>
        public string ap_invoice_no { get; set; }

        /// <summary>
        /// Trans No.：与 <see cref="ap_invoice_no"/> 同源（ah_transactionnum），
        /// 只是按前端列名再给一份，免得两边字段名对不上。
        /// </summary>
        public string trans_no { get; set; }

        /// <summary>AP 发票日期 AccTransactionHeader.ah_invoicedate</summary>
        public DateTime? ap_invoice_date { get; set; }

        /// <summary>
        /// AP 发票是否已作废 AccTransactionHeader.ah_iscancelled（1 = 已作废）。
        /// 作废的发票不过滤掉，发票号/日期照常带出，由前端按本字段展示状态 ——
        /// 与草稿箱 QueryDraftPage 不过滤 ah_iscancelled 的口径一致。未链接发票时为 null。
        /// </summary>
        public int? ap_invoice_is_cancelled { get; set; }

        /// <summary>
        /// 已过账(ah_postdate 有值) = N；未链接发票或仍是草稿 = Y。
        /// 与 shipment 侧 <see cref="Billing.Dto.BillingChargeLineItem.Draft"/> 同口径。
        /// </summary>
        public string Draft { get; set; }

        /// <summary>子行：该成本行按运单分摊出来的费用（JobCharge.jr_e6 = 本行 E6_PK）</summary>
        public List<ConsolCostItemDto> cost_items { get; set; } = new();
    }

    /// <summary>
    /// 子行：合单成本按运单分摊出来的费用行（JobCharge），挂在运单的作业头上
    /// （JobHeader.jh_parenttablecode = 'JS'）。
    /// </summary>
    public class ConsolCostItemDto
    {
        /// <summary>所属主行 JobConsolCost.E6_PK（分组用，前端一般不展示）</summary>
        public string e6_pk { get; set; }

        /// <summary>JobCharge.jr_pk</summary>
        public string jr_pk { get; set; }

        /// <summary>作业头 JobCharge.jr_jh</summary>
        public string jr_jh { get; set; }

        /// <summary>运单 pk（JobHeader.jh_parentid）</summary>
        public string js_pk { get; set; }

        /// <summary>运单号 JobShipment.js_uniqueconsignref</summary>
        public string shipment_no { get; set; }

        /// <summary>费用说明 jr_desc</summary>
        public string jr_desc { get; set; }

        /// <summary>本位币分摊金额 jr_localcostamt</summary>
        public decimal? local_cost_amount { get; set; }

        /// <summary>原币分摊金额 jr_oscostamt</summary>
        public decimal? os_cost_amount { get; set; }

        /// <summary>原币币种 jr_rx_nkcostcurrency</summary>
        public string currency { get; set; }

        /// <summary>
        /// 该子行的已开票链接 jr_al_apline（AccTransactionLines.al_pk）。
        /// 只给出 pk 不反查发票号：AccTransactionLines 有 203 万行且 al_pk 上没有索引，
        /// join 它会全表扫。发票信息看主行的 ap_invoice_no / ap_invoice_date / Draft。
        /// </summary>
        public string ap_line_pk { get; set; }
    }
}
