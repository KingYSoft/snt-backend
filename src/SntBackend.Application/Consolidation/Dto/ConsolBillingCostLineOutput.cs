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

        /// <summary>
        /// Tax Amount：与 <see cref="os_gst_amount"/> 同源（E6_OSGSTAmount，原币税额），
        /// 只是按前端列名再给一份。币种见 <see cref="currency"/>。
        /// 子行上的同名字段取自 JobCharge，见 <see cref="ConsolCostItemDto.tax_amount"/>。
        /// </summary>
        public decimal? tax_amount { get; set; }

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

        // ── 以下 4 组是合单级货量，取自 JobConsol 本身，与具体成本行无关 ──
        // 同一次查询里每行的值都一样（前端账单表格要按行展示，所以逐行给一份，
        // 而不是放在 ConsolBillingCostLineOutput 上）。合单查不到时全为 null。

        /// <summary>
        /// Container Count：该合单下的集装箱数 COUNT(JobContainer WHERE JC_JK = jk_pk)。
        /// 合单的箱直接挂 JC_JK，不经运单，与发票 PDF 的取法一致。
        /// </summary>
        public int? container_count { get; set; }

        /// <summary>
        /// 毛重：该合单下各运单 SUM(JobShipment.js_actualweight)，经 JobConShipLink 汇总。
        /// 不取 JobConsol.JK_TotalShipmentActWeightCheck —— 那列全库 40353 行只有 1 行非零。
        /// </summary>
        public decimal? gross_weight { get; set; }

        /// <summary>
        /// 毛重单位 JobShipment.js_unitofweight（如 KG）。各运单单位不一致时为 null
        /// （实测 40272 个合单没有一个出现混合单位）。
        /// </summary>
        public string gross_weight_unit { get; set; }

        /// <summary>体积 CBM：SUM(JobShipment.js_actualvolume)，来源同 <see cref="gross_weight"/></summary>
        public decimal? cbm { get; set; }

        /// <summary>体积单位 JobShipment.js_unitofvolume（如 M3），规则同 <see cref="gross_weight_unit"/></summary>
        public string cbm_unit { get; set; }

        /// <summary>
        /// 计费重：SUM(JobShipment.js_actualchargeable)。JobShipment 上没有单独的计费重单位列，
        /// 空运时单位同 <see cref="gross_weight_unit"/>，海运是计费吨 —— 不硬拼单位。
        /// </summary>
        public decimal? chargeable_weight { get; set; }

        /// <summary>子行：该成本行按运单分摊出来的费用（JobCharge.jr_e6 = 本行 E6_PK）</summary>
        public List<ConsolCostItemDto> cost_items { get; set; } = new();
    }

    /// <summary>
    /// 合单级货量（一个合单一行），用来填充 <see cref="ConsolBillingCostLineItem"/> 上
    /// container_count / gross_weight / cbm / chargeable_weight 这几列。
    /// </summary>
    public class ConsolCargoSummaryDto
    {
        public int? container_count { get; set; }
        public decimal? gross_weight { get; set; }
        public string gross_weight_unit { get; set; }
        public decimal? cbm { get; set; }
        public string cbm_unit { get; set; }
        public decimal? chargeable_weight { get; set; }
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

        // ── 计价：数量 / 单位 / 单价 ──
        // JobCharge 上没有这三列（jr_productquantity 全库 55 万行全是 0），只能取计价依据表
        // JobPaymentBasis（PBS_JR = jr_pk）。注意合单这条链路不能像 shipment 账单页那样按
        // PBS_IsCost = 1 取成本侧：实测挂在合单分摊费用行上的 337 条计价依据里，332 条是
        // 卖价侧（PBS_IsCost = 0），成本侧只有 5 条，按成本侧过滤这三列会全是 null。
        // 取法见 ConsolCostQuery.AttachCostItemsAsync 里的注释。
        //
        // 覆盖率：全库 3606 条合单分摊费用行只有 328 条（9%）挂了计价依据，
        // 其余行这三列本来就没有数据 —— 前端拿到 null 属于正常，不是接口漏返回。

        /// <summary>
        /// 数量 SUM(JobPaymentBasis.PBS_ChargeableAmount)。
        /// 成本侧有就取成本侧，否则取卖价侧 —— 数量是物理量（如 2 × 40HC），两侧相同。
        /// </summary>
        public decimal? qty { get; set; }

        /// <summary>
        /// 计价单位 JobPaymentBasis.PBS_ChargeableUnit（实测是箱型，如 40HC），取法同 <see cref="qty"/>。
        /// 混装多档费率时为 null。
        /// </summary>
        public string unit { get; set; }

        /// <summary>
        /// 单价。优先取成本侧计价依据的 PBS_PerUnitRate；成本侧没有报价档时，
        /// 按 jr_oscostamt / <see cref="qty"/> 反算，此时 <see cref="unit_price_is_derived"/> = true。
        /// 混装多档费率时为 null。
        /// </summary>
        public decimal? unit_price { get; set; }

        /// <summary>
        /// true = <see cref="unit_price"/> 是按 成本金额 / 数量 反算出来的，不是供应商报的价。
        /// 单价两侧本来就不同（实测 Ocean Freight 成本 3800 / 卖价 4250），所以宁可反算也不借卖价充数。
        /// 前端如需区分展示（比如加个 ~ 号或提示），看这个字段。
        /// </summary>
        public bool? unit_price_is_derived { get; set; }

        /// <summary>
        /// 该子行的计价依据条数（取实际用到的那一侧）。1 = 单一费率；&gt;1 = 混装多档
        /// （此时 <see cref="qty"/> 是合计，<see cref="unit"/> / <see cref="unit_price"/> 置空，
        /// 前端提示"多档费率"）；0 = 没有计价依据。
        /// </summary>
        public int? rating_line_count { get; set; }

        /// <summary>税代码 jr_at_costgstrate → AccTaxRate.AT_Code（AP 侧取成本税率，不是 jr_at_sellgstrate）</summary>
        public string tax_code { get; set; }

        /// <summary>税代码描述 AccTaxRate.AT_Description</summary>
        public string tax_desc { get; set; }

        /// <summary>
        /// Tax Amount：原币成本税额 jr_oscostgstamt，币种见 <see cref="currency"/>。
        ///
        /// 实测该值全库恒为 0，但这是**正确结果不是缺数据**：合单分摊出来的 3606 条费用行里
        /// 3598 条税码是 EXEMPT(Exempt Rated，免税)、8 条无税码，免税自然没有税额。
        /// 三处税额口径一致互相印证：jr_oscostgstamt、JobConsolCost.E6_OSGSTAmount、
        /// 对应发票行 AccTransactionLines.AL_GSTVAT 全部为 0。
        ///
        /// 不走 AL_GSTVAT 的另一个原因：那张表 203 万行且 al_pk 上无索引，
        /// 按 <see cref="ap_line_pk"/> join 会全表扫（见 AttachCostItemsAsync 的说明）。
        /// </summary>
        public decimal? tax_amount { get; set; }

        // ── 以下是该子行所属运单的货量，取自 JobShipment / 该运单的集装箱 ──
        // 同一运单的多条费用子行值相同。主行上的同名字段是合单级合计，两者口径不同。

        /// <summary>
        /// Container Count：该运单的集装箱数
        /// COUNT(DISTINCT JobContainer) 经 JobPackLines(JL_JS = js_pk) → JobContainerPackPivot。
        /// 运单的箱是拼箱链路，与合单直接挂 JC_JK 不同。
        /// </summary>
        public int? container_count { get; set; }

        /// <summary>毛重 JobShipment.js_actualweight</summary>
        public decimal? gross_weight { get; set; }

        /// <summary>毛重单位 JobShipment.js_unitofweight</summary>
        public string gross_weight_unit { get; set; }

        /// <summary>体积 CBM JobShipment.js_actualvolume</summary>
        public decimal? cbm { get; set; }

        /// <summary>体积单位 JobShipment.js_unitofvolume</summary>
        public string cbm_unit { get; set; }

        /// <summary>
        /// 计费重 JobShipment.js_actualchargeable。JobShipment 上没有单独的计费重单位列，
        /// 空运时单位同 <see cref="gross_weight_unit"/>，海运是计费吨 —— 与发票 PDF 同口径，不硬拼单位。
        /// </summary>
        public decimal? chargeable_weight { get; set; }

        /// <summary>
        /// 该子行的已开票链接 jr_al_apline（AccTransactionLines.al_pk）。
        /// 只给出 pk 不反查发票号：AccTransactionLines 有 203 万行且 al_pk 上没有索引，
        /// join 它会全表扫。发票信息看主行的 ap_invoice_no / ap_invoice_date / Draft。
        /// </summary>
        public string ap_line_pk { get; set; }
    }
}
