using Abp.Dependency;
using Dapper;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Consolidation.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Consolidation
{
    /// <summary>
    /// 合单 AP 成本行查询。
    ///
    /// 与 <see cref="Billing.BillingCore"/> 走的链路不同：BillingCore 的合单作用域读的是
    /// 锚点 → JobHeader(jh_parenttablecode='JK') → JobCharge，而库里这种作业头一条都没有
    /// （实测 0 条，'JS' 侧 45206 条），合单的成本数据全在 JobConsolCost 上：
    ///
    ///   JobConsol.jk_pk
    ///     └── JobConsolCost   E6_ParentID = jk_pk, E6_ParentTableCode = 'JK'   ← 合单级 AP 主行
    ///           ├── E6_AH_APInvoice ──> AccTransactionHeader.ah_pk
    ///           └── 分摊 ──> JobCharge.jr_e6 = E6_PK                           ← 按运单的子行
    ///                           └── jr_jh ──> JobHeader('JS') ──> JobShipment
    ///
    /// 所以这份查询独立于 BillingCore：BillingCore 的契约是"一份代码给 shipment 与合单共用"，
    /// 而这套结构只对合单成立，塞进去会破坏那个约定。
    ///
    /// JobConsolCost 只有成本侧列（无卖价），因此合单账单只支持 AP。
    /// </summary>
    public class ConsolCostQuery : ITransientDependency
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public ConsolCostQuery(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

        /// <summary>
        /// 主行筛选条件：该合单下的 AP 成本行。
        /// 不带 E6_IsValid 过滤 —— 实测该列会把要展示的存量数据滤掉，按业务要求保留全部。
        /// </summary>
        private const string CostWhere = @"
WHERE e6.E6_ParentID = @jkPk
    AND e6.E6_ParentTableCode = 'JK'";

        /// <summary>
        /// E6_ParentID / E6_PK / jr_e6 等列都是 uniqueidentifier，按 Guid 传参避免隐式转换
        /// （否则每行都要转换且用不上索引）。与 <see cref="Billing.BillingCore"/> 的 AddAnchorPk 同做法。
        /// </summary>
        private static void AddJkPk(DynamicParameters dp, string jkPk)
        {
            if (Guid.TryParse(jkPk, out var g))
                dp.Add("jkPk", g, System.Data.DbType.Guid);
            else
                dp.Add("jkPk", jkPk);
        }

        /// <summary>
        /// 按合单分页查询 AP 成本主行，并给本页每行挂上按运单分摊的子行。
        /// </summary>
        public async Task<ConsolBillingCostLineOutput> QueryCostLineAsync(string jkPk,
            int skipCount, int maxResultCount, string sorting)
        {
            if (string.IsNullOrWhiteSpace(jkPk))
                throw new Exception("jkPk cannot be empty.");

            var orderBy = !string.IsNullOrWhiteSpace(sorting) &&
                          sorting.IndexOf("DESC", StringComparison.OrdinalIgnoreCase) >= 0
                ? "ORDER BY e6.E6_Sequence DESC, e6.E6_PK DESC"
                : "ORDER BY e6.E6_Sequence, e6.E6_PK";

            var dp = new DynamicParameters();
            AddJkPk(dp, jkPk);
            dp.Add("skipCount", skipCount);
            dp.Add("takeCount", maxResultCount);

            var totalSql = $@"
SELECT COUNT(*)
FROM JobConsolCost e6
{CostWhere}
";
            var pageSql = $@"
SELECT
    e6.E6_PK                   AS e6_pk,
    e6.E6_Sequence             AS sequence,
    e6.E6_AC_ChargeCode        AS e6_ac,
    cc.ac_code                 AS charge_code,
    cc.ac_desc                 AS charge_desc,
    e6.E6_Description          AS description,
    e6.E6_RX_NKCurrency        AS currency,
    e6.E6_OSCostAmount         AS os_cost_amount,
    e6.E6_OSGSTAmount          AS os_gst_amount,
    -- Tax Amount：与 os_gst_amount 同源，按前端列名再给一份
    e6.E6_OSGSTAmount          AS tax_amount,
    e6.E6_ExchangeRate         AS exchange_rate,
    e6.E6_LocalCostAmount      AS local_cost_amount,
    e6.E6_OH_Creditor          AS creditor_oh,
    cr.oh_code                 AS creditor_code,
    cr.oh_fullname             AS creditor_name,
    e6.E6_CostReference        AS cost_reference,
    e6.E6_InvoiceNum           AS invoice_num,
    e6.E6_InvoiceDate          AS invoice_date,
    e6.E6_ApportionmentMethod  AS apportionment_method,
    e6.E6_A9_VATClass          AS vat_class,
    e6.E6_PaymentDate          AS payment_date,
    e6.E6_PaymentType          AS payment_type,
    e6.E6_AH_APInvoice         AS ap_invoice_pk,
    ap.ah_transactionnum       AS ap_invoice_no,
    ap.ah_transactionnum       AS trans_no,
    ap.ah_invoicedate          AS ap_invoice_date,
    ap.ah_iscancelled          AS ap_invoice_is_cancelled,
    -- 税代码：E6_AT_TaxRate 实测 1348 行里 1344 行有值，而 E6_A9_VATClass 全空
    at1.AT_Code                AS tax_code,
    at1.AT_Description         AS tax_desc,
    -- 分公司/部门：JobConsolCost 上没有（E6_GB_CostTaxBranch 实测全空），
    -- 取所链接 AP 发票头上的 ah_gb / ah_ge（AP 侧 95803 张全部有值）
    gb.GB_Code                 AS branch_code,
    gb.GB_BranchName           AS branch_name,
    ge.GE_Code                 AS dept_code,
    ge.GE_Desc                 AS dept_desc,
    -- E6_Description 实测 100% 为空，展示用描述回退到费用代码描述
    COALESCE(NULLIF(e6.E6_Description, ''), cc.ac_desc) AS display_description,
    -- 数量 / 单位 / 单价：JobConsolCost 上没有这三列，它们在计价依据表 JobPaymentBasis 上
    pb.qty, pb.unit, pb.unit_price, pb.rating_line_count,
    -- 已过账(ah_postdate 有值)= N；未链接发票或仍是草稿 = Y
    CASE WHEN ap.ah_postdate IS NOT NULL THEN 'N' ELSE 'Y' END AS Draft
FROM JobConsolCost e6
LEFT JOIN AccChargeCode        cc  ON cc.ac_pk = e6.E6_AC_ChargeCode
LEFT JOIN OrgHeader            cr  ON cr.oh_pk = e6.E6_OH_Creditor
LEFT JOIN AccTaxRate           at1 ON at1.AT_PK = e6.E6_AT_TaxRate
-- 不按 ah_iscancelled 过滤：发票作废后仍要带出发票号/日期，由前端按 ap_invoice_is_cancelled
-- 展示状态。否则作废会让这几列变空、Draft 翻回 'Y'，与草稿箱列表里的作废状态对不上。
LEFT JOIN AccTransactionHeader ap  ON ap.ah_pk = e6.E6_AH_APInvoice
LEFT JOIN GlbBranch            gb  ON gb.GB_PK = ap.ah_gb
LEFT JOIN GlbDepartment        ge  ON ge.GE_PK = ap.ah_ge
-- 计价依据：一条成本行通常只有一条（实测 353 条里 347 条如此），
-- 但混装时会拆成多条且费率/单位各不相同（实测 6 条是 20GP + 40HC 两档），
-- 这种情况下把 unit / unit_price 置空，只给出合计数量，由前端按 rating_line_count 提示多档。
OUTER APPLY (
    SELECT SUM(pbs.PBS_ChargeableAmount) AS qty,
           CASE WHEN COUNT(DISTINCT pbs.PBS_ChargeableUnit) = 1
                THEN MIN(pbs.PBS_ChargeableUnit) END AS unit,
           CASE WHEN COUNT(DISTINCT pbs.PBS_PerUnitRate) = 1
                THEN MIN(pbs.PBS_PerUnitRate) END  AS unit_price,
           COUNT(*)                                AS rating_line_count
    FROM JobPaymentBasis pbs
    WHERE pbs.PBS_E6 = e6.E6_PK
) pb
{CostWhere}
{orderBy}
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";

            // 合单级货量：箱数 / 毛重 / 体积 / 计费重。与成本行无关，单独一条语句取一行再逐行
            // 填到 Items 上，避免在分页查询里为每行重复算一次。
            //
            // 毛重/体积/计费重**不能**取 JobConsol 自己那几列。实测 JobConsol 40353 行里
            // JK_TotalShipmentActWeightCheck / JK_TotalShipmentActVolumeCheck 各只有 1 行非零、
            // JK_ConsolChargeable 一行非零都没有 —— 这套 "Check" 列在本库根本没维护。
            // 改成按 JobConShipLink 汇总该合单下各运单的实际货量：JobShipment 侧
            // 43598/44851 行有毛重，汇总后 39799/40272 个合单能拿到非零值。
            // 单位取各运单一致时的那一个（实测 40272 个合单没有一个出现混合单位）。
            //
            // 箱数仍取 JobConsol 自己的箱：合单的箱直接挂 JobContainer.JC_JK 不经运单，
            // 实测 38580 个合单挂到了箱，这列是有数据的。
            //
            // 注：发票 PDF 的合单上下文（InvoicePdfDataProvider.ConsolContextSql）目前还在读
            // 那几个 JK_ 空列，所以合单发票上的 GROSS WEIGHT / CBM / CHARGEABLE 是空的。
            // 属于既有问题，不在本次改动范围内。
            var cargoSql = @"
SELECT TOP 1
    (SELECT COUNT(*) FROM JobContainer jc WHERE jc.JC_JK = jk.JK_PK) AS container_count,
    agg.gross_weight,
    agg.gross_weight_unit,
    agg.cbm,
    agg.cbm_unit,
    agg.chargeable_weight
FROM JobConsol jk
OUTER APPLY (
    SELECT SUM(js.js_actualweight)     AS gross_weight,
           CASE WHEN COUNT(DISTINCT js.js_unitofweight) = 1
                THEN MIN(js.js_unitofweight) END AS gross_weight_unit,
           SUM(js.js_actualvolume)     AS cbm,
           CASE WHEN COUNT(DISTINCT js.js_unitofvolume) = 1
                THEN MIN(js.js_unitofvolume) END AS cbm_unit,
           SUM(js.js_actualchargeable) AS chargeable_weight
    FROM JobConShipLink jn
    INNER JOIN JobShipment js ON js.js_pk = jn.jn_js
    WHERE jn.jn_jk = jk.JK_PK
) agg
WHERE jk.JK_PK = @jkPk
";

            var output = new ConsolBillingCostLineOutput();
            ConsolCargoSummaryDto cargo = null;
            using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql};
{cargoSql}
", dp))
            {
                output.TotalCount = await multi.ReadFirstAsync<int>();
                output.Items = (await multi.ReadAsync<ConsolBillingCostLineItem>()).ToList();
                cargo = (await multi.ReadAsync<ConsolCargoSummaryDto>()).FirstOrDefault();
            }

            ApplyCargoSummary(output.Items, cargo);

            await AttachCostItemsAsync(jkPk, output.Items);

            return output;
        }

        /// <summary>
        /// 把合单级货量铺到本页每一行上（同一合单每行的值相同）。合单查不到时保持 null。
        /// </summary>
        private static void ApplyCargoSummary(List<ConsolBillingCostLineItem> items, ConsolCargoSummaryDto cargo)
        {
            if (cargo == null || items == null || items.Count == 0)
                return;

            foreach (var item in items)
            {
                item.container_count = cargo.container_count;
                item.gross_weight = cargo.gross_weight;
                item.gross_weight_unit = cargo.gross_weight_unit;
                item.cbm = cargo.cbm;
                item.cbm_unit = cargo.cbm_unit;
                item.chargeable_weight = cargo.chargeable_weight;
            }
        }

        /// <summary>
        /// 该合单下所有运单的作业头 pk。
        /// 用来给子行查询做主过滤条件：jr_e6 上没有索引，只按它过滤会全扫 JobCharge（55 万行），
        /// 换成 jr_jh（JobCharge 的父表外键，通常有索引）后只命中该合单那几条运单的费用。
        /// </summary>
        private async Task<List<Guid>> ShipmentJobHeaderPksAsync(string jkPk)
        {
            var dp = new DynamicParameters();
            AddJkPk(dp, jkPk);

            var rows = await _appSqlServerRepository.QueryAsync<Guid?>(@"
SELECT DISTINCT jh.jh_pk
FROM JobConShipLink jn
INNER JOIN JobHeader jh ON jh.jh_parentid = jn.jn_js AND jh.jh_parenttablecode = 'JS'
WHERE jn.jn_jk = @jkPk
", dp);

            return rows.Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();
        }

        /// <summary>
        /// 给本页主行批量挂子行：一条 SQL 取完再在内存按 e6_pk 分组，避免 N+1。
        /// </summary>
        private async Task AttachCostItemsAsync(string jkPk, List<ConsolBillingCostLineItem> items)
        {
            if (items == null || items.Count == 0)
                return;

            var e6Pks = items
                .Select(x => Guid.TryParse(x.e6_pk, out var g) ? g : (Guid?)null)
                .Where(x => x.HasValue)
                .Select(x => x.Value)
                .Distinct()
                .ToList();

            if (e6Pks.Count == 0)
                return;

            var jhPks = await ShipmentJobHeaderPksAsync(jkPk);
            if (jhPks.Count == 0)
                return;

            var dp = new DynamicParameters();
            dp.Add("jhPks", jhPks);

            // 访问路径全部按实测索引选定（都是 QI1PRD 上 sys.indexes 的实际情况）：
            //  · jr_jh 是 IX_JobCharge_QueryChargeLine_Job_Order 的首键列，走 seek；该索引的 INCLUDE
            //    已覆盖本查询要的 JR_LocalCostAmt / JR_OSCostAmt / JR_RX_NKCostCurrency /
            //    JR_AL_APLine / JR_Desc。
            //  · jr_e6 不在该索引的键列或 INCLUDE 里，进 WHERE 会让优化器放弃索引改全表扫
            //    （JobCharge 55 万行），所以按 e6_pk 的筛选留到内存做 —— 单合单的运单费用只有几十行。
            //  · 不 join AccTransactionLines / AccTransactionHeader：AccTransactionLines 有 203 万行
            //    且 al_pk 上没有任何索引（只有 (AL_AH,AL_Sequence) 与 (AL_JH,AL_AH)），
            //    按 al_pk join 会全表扫并直接把这个查询拖死。发票信息由主行的 E6_AH_APInvoice 提供。
            // 追加的计价 / 税 / 货量列都是每合单几十行级别的开销：
            //  · jr_at_costgstrate / jr_oscostgstamt 不在上述索引的 INCLUDE 里，会多一次键查找，
            //    但行数就这么点，比起换索引更划算；AccTaxRate 是小码表，join 可忽略。
            //  · 计价依据走 ChargeRatingSql.ByCharge（PBS_JR + PBS_IsCost=1，AP 侧），与 shipment
            //    账单页同一份 SQL 片段，保证两个页面的 Qty/Unit/Unit Price 口径一致。
            //  · 箱数按运单的拼箱链路 JobPackLines(JL_JS) → JobContainerPackPivot → JobContainer 数，
            //    不是合单那条 JC_JK；两者不是一回事，主行给的才是合单级箱数。
            var sql = $@"
SELECT
    jr.jr_e6                   AS e6_pk,
    jr.jr_pk                   AS jr_pk,
    jr.jr_jh                   AS jr_jh,
    jh.jh_parentid             AS js_pk,
    js.js_uniqueconsignref     AS shipment_no,
    jr.jr_desc                 AS jr_desc,
    jr.jr_localcostamt         AS local_cost_amount,
    jr.jr_oscostamt            AS os_cost_amount,
    jr.jr_rx_nkcostcurrency    AS currency,
    jr.jr_al_apline            AS ap_line_pk,
    -- 税：AP 侧取成本税率/成本税额，不是 sell 那一组
    at1.AT_Code                AS tax_code,
    at1.AT_Description         AS tax_desc,
    jr.jr_oscostgstamt         AS tax_amount,
    -- 数量 / 单位 / 单价：JobCharge 上没有，取计价依据表。取法见下方 OUTER APPLY 的说明。
    COALESCE(pb.qty_cost,  pb.qty_sell)  AS qty,
    COALESCE(pb.unit_cost, pb.unit_sell) AS unit,
    -- 反算的商在 SQL 里会带一长串小数（136.0000000000000000000），统一收成 4 位再出去
    CAST(COALESCE(pb.unit_price_cost,
                  CASE WHEN COALESCE(pb.qty_cost, pb.qty_sell) > 0
                       THEN jr.jr_oscostamt / COALESCE(pb.qty_cost, pb.qty_sell) END)
         AS decimal(19, 4)) AS unit_price,
    -- 单价是按 成本金额 / 数量 反算出来的（成本侧没报价档时），前端别当成对方的报价展示
    CAST(CASE WHEN pb.unit_price_cost IS NULL AND COALESCE(pb.qty_cost, pb.qty_sell) > 0
              THEN 1 ELSE 0 END AS bit) AS unit_price_is_derived,
    CASE WHEN pb.cost_cnt > 0 THEN pb.cost_cnt ELSE pb.sell_cnt END AS rating_line_count,
    -- 该子行所属运单的货量
    js.js_actualweight         AS gross_weight,
    js.js_unitofweight         AS gross_weight_unit,
    js.js_actualvolume         AS cbm,
    js.js_unitofvolume         AS cbm_unit,
    js.js_actualchargeable     AS chargeable_weight,
    ct.container_count         AS container_count
FROM JobCharge jr
INNER JOIN JobHeader  jh ON jh.jh_pk = jr.jr_jh
LEFT JOIN JobShipment js ON js.js_pk = jh.jh_parentid
LEFT JOIN AccTaxRate  at1 ON at1.AT_PK = jr.jr_at_costgstrate
-- 计价依据。这里不能直接用 ChargeRatingSql.ByCharge 的 AP 侧片段：那个片段按
-- PBS_IsCost = 1 只取成本侧，而合单分摊出来的费用行实测几乎没有成本侧计价依据 ——
-- 337 条挂在 jr_e6 非空费用行上的 JobPaymentBasis 里，332 条是 PBS_IsCost = 0（卖价侧），
-- 成本侧只有 5 条。按成本侧过滤等于把这三列全滤成 null（前端反馈的就是这个现象）。
--
-- 取法：数量与单位是物理量（如 2 × 40HC），两侧本来就一样，成本侧没有就用卖价侧；
-- 单价两侧不同（实测 Ocean Freight 成本 3800 / 卖价 4250），只认成本侧的报价档，
-- 没有就按 成本金额 / 数量 反算，并用 unit_price_is_derived = 1 标出来，
-- 免得前端把反算值当成供应商报价。shipment 侧成本行有正常的 IsCost = 1 数据，
-- 那边继续用 ChargeRatingSql，不受本处影响。
OUTER APPLY (
    SELECT
        SUM(CASE WHEN pbs.PBS_IsCost = 1 THEN pbs.PBS_ChargeableAmount END) AS qty_cost,
        SUM(CASE WHEN pbs.PBS_IsCost = 0 THEN pbs.PBS_ChargeableAmount END) AS qty_sell,
        -- 混装多档（同一行几种箱型/费率）时置空，只保留合计数量，由 rating_line_count 提示
        CASE WHEN COUNT(DISTINCT CASE WHEN pbs.PBS_IsCost = 1 THEN pbs.PBS_ChargeableUnit END) = 1
             THEN MIN(CASE WHEN pbs.PBS_IsCost = 1 THEN pbs.PBS_ChargeableUnit END) END AS unit_cost,
        CASE WHEN COUNT(DISTINCT CASE WHEN pbs.PBS_IsCost = 0 THEN pbs.PBS_ChargeableUnit END) = 1
             THEN MIN(CASE WHEN pbs.PBS_IsCost = 0 THEN pbs.PBS_ChargeableUnit END) END AS unit_sell,
        CASE WHEN COUNT(DISTINCT CASE WHEN pbs.PBS_IsCost = 1 THEN pbs.PBS_PerUnitRate END) = 1
             THEN MIN(CASE WHEN pbs.PBS_IsCost = 1 THEN pbs.PBS_PerUnitRate END) END AS unit_price_cost,
        SUM(CASE WHEN pbs.PBS_IsCost = 1 THEN 1 ELSE 0 END) AS cost_cnt,
        SUM(CASE WHEN pbs.PBS_IsCost = 0 THEN 1 ELSE 0 END) AS sell_cnt
    FROM JobPaymentBasis pbs
    WHERE pbs.PBS_JR = jr.jr_pk
) pb
OUTER APPLY (
    SELECT COUNT(DISTINCT p.J6_JC) AS container_count
    FROM JobPackLines jl
    INNER JOIN JobContainerPackPivot p ON p.J6_JL = jl.jl_pk
    WHERE jl.jl_js = jh.jh_parentid
) ct
WHERE jr.jr_jh IN @jhPks
    AND jr.jr_e6 IS NOT NULL
ORDER BY js.js_uniqueconsignref, jr.jr_pk
";

            var wanted = new HashSet<Guid>(e6Pks);
            var children = (await _appSqlServerRepository.QueryAsync<ConsolCostItemDto>(sql, dp))
                .Where(x => Guid.TryParse(x.e6_pk, out var g) && wanted.Contains(g))
                .ToList();
            if (children.Count == 0)
                return;

            var map = children
                .Where(x => !string.IsNullOrWhiteSpace(x.e6_pk))
                .GroupBy(x => x.e6_pk, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.e6_pk) && map.TryGetValue(item.e6_pk, out var list))
                    item.cost_items = list;
            }
        }

        /// <summary>
        /// 按合单分页查询 AP 发票头（草稿箱）。
        ///
        /// 合单到发票的唯一通路是成本行上的外键：
        ///   JobConsolCost(E6_ParentID = jkPk, E6_ParentTableCode = 'JK').E6_AH_APInvoice
        ///     └──> AccTransactionHeader.ah_pk
        /// 而不是 <see cref="Billing.BillingCore"/> 走的 ah_jh -> JobHeader('JK')：库里这种作业头
        /// 一条都没有（见本类头部注释），那条链路在合单侧恒为空。
        ///
        /// 多条成本行可以指向同一张发票，按 ah_pk 分组去重后每张发票一行；
        /// 分组时顺带算出 apportioned_local_amount（见该字段注释：跨合单发票的本单归属额）。
        /// 已过账的发票一并返回，用 Draft 列区分，与 <see cref="QueryCostLineAsync"/> 同口径；
        /// 已作废的同样一并返回（不过滤 ah_iscancelled），由前端按该列展示状态。
        /// </summary>
        public async Task<BillingDraftPageOutput> QueryDraftPageAsync(string jkPk,
            int skipCount, int maxResultCount, string sorting)
        {
            if (string.IsNullOrWhiteSpace(jkPk))
                throw new Exception("jkPk cannot be empty.");

            var orderBy = !string.IsNullOrWhiteSpace(sorting) &&
                          sorting.IndexOf("DESC", StringComparison.OrdinalIgnoreCase) >= 0
                ? "ORDER BY ah.ah_invoicedate DESC, ah.ah_pk DESC"
                : "ORDER BY ah.ah_invoicedate, ah.ah_pk";

            var dp = new DynamicParameters();
            AddJkPk(dp, jkPk);
            dp.Add("skipCount", skipCount);
            dp.Add("takeCount", maxResultCount);

            // CTE 的作用域只到紧跟的那条语句，两条语句各写一份。
            // 取负号：E6_LocalCostAmount 存的是正数，而 AP 在 AccTransactionHeader 里存负数
            // （实测同一行 ah_invoiceamount = -96047 对分摊额 4302）。同一行上两个金额必须同号，
            // 否则前端并排展示或做占比会出错。注意由此与 GetApSummaryAsync 返回的正数符号相反。
            const string invoiceCte = @"
WITH inv AS (
    SELECT e6.E6_AH_APInvoice           AS ah_pk,
           -SUM(e6.E6_LocalCostAmount)  AS apportioned_local_amount
    FROM JobConsolCost e6
    WHERE e6.E6_ParentID = @jkPk
        AND e6.E6_ParentTableCode = 'JK'
        AND e6.E6_AH_APInvoice IS NOT NULL
    GROUP BY e6.E6_AH_APInvoice
)";

            // 不按 ah_iscancelled 过滤：作废的发票也要出现在列表里，由前端按 ah_iscancelled 展示状态
            // （与 shipment 侧 BillingApplication 的 ah_iscancelled = 0 不同，对齐 first-cargo 合单草稿箱
            // 的行为，也与本类 CostWhere 不过滤 E6_IsValid 的取舍一致）。ah.* 已带出该列。
            var totalSql = $@"
{invoiceCte}
SELECT COUNT(*)
FROM inv i
INNER JOIN AccTransactionHeader ah ON ah.ah_pk = i.ah_pk
";
            var pageSql = $@"
{invoiceCte}
SELECT
    ah.*,
    o.oh_fullname,
    i.apportioned_local_amount,
{Billing.AccTransactionHeaderSql.DisplayColumns("ah")},
    CASE WHEN ah.ah_postdate IS NOT NULL THEN 'N' ELSE 'Y' END AS Draft
FROM inv i
INNER JOIN AccTransactionHeader ah ON ah.ah_pk = i.ah_pk
LEFT JOIN OrgHeader o ON o.OH_PK = ah.ah_oh
{Billing.AccTransactionHeaderSql.DisplayJoins("ah")}
{orderBy}
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";

            var output = new BillingDraftPageOutput();
            using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql}
", dp))
            {
                output.TotalCount = await multi.ReadFirstAsync<int>();
                output.Items = (await multi.ReadAsync<AccTransactionHeaderDtoOutput>()).ToList();
            }

            return output;
        }

        /// <summary>
        /// 合单 AP 合计（本位币），口径与 <see cref="QueryCostLineAsync"/> 的列表完全一致。
        /// </summary>
        public async Task<decimal> GetApSummaryAsync(string jkPk)
        {
            if (string.IsNullOrWhiteSpace(jkPk))
                throw new Exception("jkPk cannot be empty.");

            var dp = new DynamicParameters();
            AddJkPk(dp, jkPk);

            var sql = $@"
SELECT ISNULL(SUM(e6.E6_LocalCostAmount), 0)
FROM JobConsolCost e6
{CostWhere}
";
            return await _appSqlServerRepository.QueryFirstOrDefaultAsync<decimal>(sql, dp);
        }
    }
}
