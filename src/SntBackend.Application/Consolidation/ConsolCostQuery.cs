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
    ap.ah_invoicedate          AS ap_invoice_date,
    ap.ah_iscancelled          AS ap_invoice_is_cancelled,
    -- 已过账(ah_postdate 有值)= N；未链接发票或仍是草稿 = Y
    CASE WHEN ap.ah_postdate IS NOT NULL THEN 'N' ELSE 'Y' END AS Draft
FROM JobConsolCost e6
LEFT JOIN AccChargeCode        cc ON cc.ac_pk = e6.E6_AC_ChargeCode
LEFT JOIN OrgHeader            cr ON cr.oh_pk = e6.E6_OH_Creditor
-- 不按 ah_iscancelled 过滤：发票作废后仍要带出发票号/日期，由前端按 ap_invoice_is_cancelled
-- 展示状态。否则作废会让这几列变空、Draft 翻回 'Y'，与草稿箱列表里的作废状态对不上。
LEFT JOIN AccTransactionHeader ap ON ap.ah_pk = e6.E6_AH_APInvoice
{CostWhere}
{orderBy}
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";

            var output = new ConsolBillingCostLineOutput();
            using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql}
", dp))
            {
                output.TotalCount = await multi.ReadFirstAsync<int>();
                output.Items = (await multi.ReadAsync<ConsolBillingCostLineItem>()).ToList();
            }

            await AttachCostItemsAsync(jkPk, output.Items);

            return output;
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
            var sql = @"
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
    jr.jr_al_apline            AS ap_line_pk
FROM JobCharge jr
INNER JOIN JobHeader  jh ON jh.jh_pk = jr.jr_jh
LEFT JOIN JobShipment js ON js.js_pk = jh.jh_parentid
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
    CASE WHEN ah.ah_postdate IS NOT NULL THEN 'N' ELSE 'Y' END AS Draft
FROM inv i
INNER JOIN AccTransactionHeader ah ON ah.ah_pk = i.ah_pk
LEFT JOIN OrgHeader o ON o.OH_PK = ah.ah_oh
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
