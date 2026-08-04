using Abp.Dependency;
using Dapper;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Billing
{
    /// <summary>
    /// 账单核心实现：一份代码给 shipment(JobShipment) 与 合单(JobConsol) 共用。
    ///
    /// 账单链路本身与锚点无关：
    ///   锚点 → JobHeader(jh_parentid = 锚点pk, jh_parenttablecode = 'JS'/'JK')
    ///        → JobCharge / AccTransactionHeader → AccTransactionLines
    /// 两个作用域的差异只有「锚点表名 + 三个列名」，全部收敛在 <see cref="BillingScope"/>。
    ///
    /// 重要差异（snt 与 first-cargo 数据模型不同）：
    ///  1. snt 无草稿表(TTL_TEMP_LINES)。"草稿"与"已过账"统一落在 AccTransactionHeader/Lines，
    ///     以 ah_postdate 是否为空区分：NULL=草稿，有值=已过账。
    ///  2. snt 的 JobCharge 是 BTH，一行同时含 AR(sell)/AP(cost) 两侧，
    ///     这里按 chargeType 只写所选侧；jr_al_arline/jr_al_apline 为该侧已开票链接。
    ///  3. branch/company/dept 等 create-time 字段从该锚点下的 JobHeader 继承。
    ///  4. 大表 INSERT 采用 "复制模板行 + 覆盖业务列" 方式，以满足众多 NOT NULL 列。
    ///  ⚠ AP 侧金额按 snt 习惯写为负数（与现有 AP 数据一致）。
    /// </summary>
    public class BillingCore : ITransientDependency
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public BillingCore(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

        private const string SysUser = "sys";

        /// <summary>锚点主键对应的库列是 uniqueidentifier，按 Guid 传参避免隐式转换。</summary>
        private static void AddAnchorPk(DynamicParameters dp, string anchorPk)
        {
            if (Guid.TryParse(anchorPk, out var g))
                dp.Add("anchorPk", g, System.Data.DbType.Guid);
            else
                dp.Add("anchorPk", anchorPk);
        }

        // ============================================================================
        // 读操作
        // ============================================================================

        /// <summary>按锚点 + AR/AP 分页查询费用行（JobCharge，按所选侧投影）。</summary>
        public async Task<BillingChargeLineOutput> QueryChargeLineAsync(BillingScope scope, string anchorPk,
            string chargeType, int skipCount, int maxResultCount, string sorting)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(anchorPk))
                    throw new Exception($"{scope.PkParamName} cannot be empty.");
                if (string.IsNullOrWhiteSpace(chargeType))
                    throw new Exception("chargeType cannot be empty.");

                var isAr = string.Equals(chargeType, "AR", StringComparison.OrdinalIgnoreCase);
                var isAp = string.Equals(chargeType, "AP", StringComparison.OrdinalIgnoreCase);
                if (!isAr && !isAp)
                    throw new Exception("chargeType must be AR or AP.");

                // 按所选侧投影列名 + 过滤"该侧无效"行
                var amountCol = isAr ? "jr.jr_localsellamt" : "jr.jr_localcostamt";
                var osAmountCol = isAr ? "jr.jr_ossellamt" : "jr.jr_oscostamt";
                var currencyCol = isAr ? "jr.jr_rx_nksellcurrency" : "jr.jr_rx_nkcostcurrency";
                var partyCol = isAr ? "jr.jr_oh_sellaccount" : "jr.jr_oh_costaccount";
                var rateCol = isAr ? "jr.jr_ossellexrate" : "jr.jr_oscostexrate";
                var gstCol = isAr ? "jr.jr_at_sellgstrate" : "jr.jr_at_costgstrate";
                var whtCol = isAr ? "jr.jr_aw_sellwhtrate" : "jr.jr_aw_costwhtrate";
                var vatCol = isAr ? "jr.jr_a9_sellvatclass" : "jr.jr_a9_costvatclass";
                var lineCol = isAr ? "jr.jr_al_arline" : "jr.jr_al_apline";

                var sideFilter = isAr
                    ? "( jr.jr_localsellamt <> 0 OR jr.jr_al_arline IS NOT NULL OR jr.jr_oh_sellaccount IS NOT NULL )"
                    : "( jr.jr_localcostamt <> 0 OR jr.jr_al_apline IS NOT NULL OR jr.jr_oh_costaccount IS NOT NULL )";

                var orderBy = !string.IsNullOrWhiteSpace(sorting) &&
                              sorting.IndexOf("DESC", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "ORDER BY jr.jr_displaysequence DESC, jr.jr_pk DESC"
                    : "ORDER BY jr.jr_displaysequence, jr.jr_pk";

                var dp = new DynamicParameters();
                AddAnchorPk(dp, anchorPk);
                dp.Add("skipCount", skipCount);
                dp.Add("takeCount", maxResultCount);

                var anchorWhere = $@"
WHERE jh.jh_parentid = @anchorPk
    AND jh.jh_parenttablecode = '{scope.ParentTableCode}'
    AND anchor.{scope.CancelledColumn} = 0
    AND {sideFilter}";

                var totalSql = $@"
SELECT COUNT(*)
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
INNER JOIN {scope.ParentTable} anchor ON anchor.{scope.ParentPkColumn} = jh.jh_parentid
{anchorWhere}
";
                var pageSql = $@"
SELECT
    jr.jr_pk,
    jr.jr_jh,
    jr.jr_chargetype,
    jr.jr_ac         AS jr_ac,
    cc.ac_code       AS charge_code,
    cc.ac_desc       AS charge_desc,
    jr.jr_desc,
    {amountCol}    AS amount,
    {osAmountCol}  AS os_amount,
    -- 数量：优先取已开票行(al_unitqty)，否则回退 JobCharge.jr_productquantity
    COALESCE(line.al_unitqty, jr.jr_productquantity) AS qty,
    -- 单价：优先取已开票行(al_unitprice)，否则按 原币金额/数量 计算（数量为 0 时为 NULL）
    COALESCE(line.al_unitprice,
             CASE WHEN jr.jr_productquantity <> 0 THEN {osAmountCol} / jr.jr_productquantity END) AS unit_price,
    {currencyCol}  AS currency,
    {partyCol}     AS party_oh,
    {rateCol}      AS exchange_rate,
    {gstCol}       AS gst_rate,
    {whtCol}       AS wht_rate,
    {vatCol}       AS vat_class,
    jr.jr_invoicetype AS jr_invoicetype,
    jr.jr_gb          AS jr_gb,
    gb.gb_code        AS branch_code,
    gb.gb_branchname  AS branch_name,
    party.oh_code     AS party_code,
    party.oh_fullname AS party_name,
    {lineCol}      AS line_pk,
    inv.ah_pk              AS invoice_pk,
    inv.ah_transactionnum  AS invoice_no,
    inv.ah_invoicedate     AS invoice_date,
    -- 已过账(ah_postdate 有值)= N；未链接或仍是草稿(postdate 为空)= Y
    CASE WHEN inv.ah_postdate IS NOT NULL THEN 'N' ELSE 'Y' END AS Draft
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
INNER JOIN {scope.ParentTable} anchor ON anchor.{scope.ParentPkColumn} = jh.jh_parentid
LEFT JOIN AccTransactionLines line ON line.al_pk = {lineCol}
LEFT JOIN AccTransactionHeader inv ON inv.ah_pk = line.al_ah AND inv.ah_iscancelled = 0
LEFT JOIN GlbBranch gb ON gb.gb_pk = jr.jr_gb
LEFT JOIN OrgHeader party ON party.oh_pk = {partyCol}
LEFT JOIN AccChargeCode cc ON cc.ac_pk = jr.jr_ac
{anchorWhere}
{orderBy}
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";

                var output = new BillingChargeLineOutput();
                using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql}
", dp))
                {
                    output.TotalCount = await multi.ReadFirstAsync<int>();
                    output.Items = (await multi.ReadAsync<BillingChargeLineItem>()).ToList();
                }

                return output;
            }
            catch (Exception ex)
            {
                DumpError("QueryChargeLine", ex);
                throw;
            }
        }

        /// <summary>
        /// 按锚点 + AR/AP 分页查询发票头。
        /// 类型含 INV(发票) 与 CRD(贷项通知/红字发票)。
        /// 注：作废已过账账单生成的那种冲销 CRD 自身是 ah_iscancelled = 1，会被这里的条件排除；
        /// 能查出来的 CRD 是仍然有效的贷项通知单。
        /// </summary>
        public async Task<BillingDraftPageOutput> QueryDraftPageAsync(BillingScope scope, string anchorPk,
            string chargeType, int skipCount, int maxResultCount, string sorting)
        {
            if (string.IsNullOrWhiteSpace(anchorPk))
                throw new Exception($"{scope.PkParamName} cannot be empty.");

            var orderBy = !string.IsNullOrWhiteSpace(sorting) &&
                          sorting.IndexOf("DESC", StringComparison.OrdinalIgnoreCase) >= 0
                ? "ORDER BY ah.ah_invoicedate DESC, ah.ah_pk DESC"
                : "ORDER BY ah.ah_invoicedate, ah.ah_pk";

            var dp = new DynamicParameters();
            AddAnchorPk(dp, anchorPk);
            dp.Add("skipCount", skipCount);
            dp.Add("takeCount", maxResultCount);

            var ledgerWhere = "";
            if (!string.IsNullOrWhiteSpace(chargeType))
            {
                ledgerWhere = " AND ah.ah_ledger = @chargeType ";
                dp.Add("chargeType", chargeType);
            }

            var anchorWhere = $@"
WHERE jh.jh_parentid = @anchorPk
    AND jh.jh_parenttablecode = '{scope.ParentTableCode}'
    AND anchor.{scope.CancelledColumn} = 0
    AND ah.ah_iscancelled = 0
    AND ah.ah_transactiontype IN ('INV', 'CRD')
    {ledgerWhere}";

            var totalSql = $@"
SELECT COUNT(*)
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
INNER JOIN {scope.ParentTable} anchor ON anchor.{scope.ParentPkColumn} = jh.jh_parentid
{anchorWhere}
";
            var pageSql = $@"
SELECT ah.*
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
INNER JOIN {scope.ParentTable} anchor ON anchor.{scope.ParentPkColumn} = jh.jh_parentid
{anchorWhere}
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

        /// <summary>账单汇总：按 JobCharge 两侧本位币金额汇总（AR/AP/利润/毛利率）。</summary>
        public async Task<BillingSummaryDto> GetBillingSummaryAsync(BillingScope scope, string anchorPk)
        {
            if (string.IsNullOrWhiteSpace(anchorPk))
                throw new Exception($"{scope.PkParamName} cannot be empty.");

            var dp = new DynamicParameters();
            AddAnchorPk(dp, anchorPk);

            var sql = $@"
SELECT
    ISNULL(SUM(jr.jr_localsellamt), 0) AS ar,
    ISNULL(SUM(jr.jr_localcostamt), 0) AS ap
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
INNER JOIN {scope.ParentTable} anchor ON anchor.{scope.ParentPkColumn} = jh.jh_parentid
WHERE jh.jh_parentid = @anchorPk
    AND jh.jh_parenttablecode = '{scope.ParentTableCode}'
    AND anchor.{scope.CancelledColumn} = 0
    AND jr.jr_isvalid = 1
";
            var row = await _appSqlServerRepository.QueryFirstOrDefaultAsync<(decimal ar, decimal ap)>(sql, dp);

            var ar = row.ar;
            var ap = row.ap;
            var profits = ar - ap;
            var grossProfitMargin = ar > 0 ? profits / ar * 100 : 0;

            return new BillingSummaryDto
            {
                ar = Math.Round(ar, 2),
                ap = Math.Round(ap, 2),
                profits = Math.Round(profits, 2),
                grossProfitMargin = Math.Round(grossProfitMargin, 2),
                home_currency = ""
            };
        }

        // ============================================================================
        // 写操作（新增 / 修改 / 生成草稿 / 过账 / 作废）
        // ============================================================================

        /// <summary>
        /// 锚点的发票号序列：返回 (号码基础串, 已用到的最大后缀序号)。
        /// 下一张发票号 = $"{consignRef}/{GenerateInvoiceSuffix(maxIndex + 1)}"；
        /// 该锚点下还没有任何发票时 maxIndex = -1（下一张是 /A）。
        /// </summary>
        private async Task<(string consignRef, int maxIndex)> InvoiceSeriesAsync(BillingScope scope,
            string anchorPk, string fallbackJobNum)
        {
            // 发票号基础串：锚点 consign ref
            var refDp = new DynamicParameters();
            AddAnchorPk(refDp, anchorPk);
            var consignRef = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                $@"SELECT TOP 1 {scope.ConsignRefColumn} FROM {scope.ParentTable}
WHERE {scope.ParentPkColumn} = @anchorPk", refDp);
            if (string.IsNullOrWhiteSpace(consignRef))
                consignRef = fallbackJobNum ?? "INV";

            // 该锚点下已有发票号，算下一个后缀
            var existDp = new DynamicParameters();
            AddAnchorPk(existDp, anchorPk);
            var existingNos = (await _appSqlServerRepository.QueryAsync<string>($@"
SELECT ah.ah_transactionnum
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
WHERE jh.jh_parentid = @anchorPk AND jh.jh_parenttablecode = '{scope.ParentTableCode}'
    AND ah.ah_transactionnum IS NOT NULL
", existDp)).ToList();

            var prefix = consignRef + "/";
            var maxIndex = -1;
            foreach (var no in existingNos)
            {
                if (no != null && no.StartsWith(prefix))
                {
                    var idx = ParseInvoiceSuffix(no.Substring(prefix.Length));
                    if (idx > maxIndex) maxIndex = idx;
                }
            }

            return (consignRef, maxIndex);
        }

        /// <summary>0->A 1->B ... 25->Z 26->AA ...</summary>
        private static string GenerateInvoiceSuffix(int index)
        {
            var result = "";
            var n = index;
            do
            {
                result = (char)('A' + n % 26) + result;
                n = n / 26 - 1;
            } while (n >= 0);
            return result;
        }

        /// <summary>A->0 B->1 ... Z->25 AA->26 ...；非法返回 -1</summary>
        private static int ParseInvoiceSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return -1;
            var result = 0;
            foreach (var c in suffix.ToUpperInvariant())
            {
                if (c < 'A' || c > 'Z') return -1;
                result = result * 26 + (c - 'A' + 1);
            }
            return result - 1;
        }

        /// <summary>
        /// 定位锚点下的作业头（create-time 字段从它继承）。
        /// 合单作用域下若不存在则按需创建一条（snt 库里合单作业头可能尚未建）；
        /// shipment 作用域下不存在直接报错。
        /// </summary>
        private async Task<JobHeaderCtx> EnsureJobHeaderAsync(BillingScope scope, string anchorPk)
        {
            var jobDp = new DynamicParameters();
            AddAnchorPk(jobDp, anchorPk);
            var job = await _appSqlServerRepository.QueryFirstOrDefaultAsync<JobHeaderCtx>($@"
SELECT TOP 1 jh.jh_pk, jh.jh_gb, jh.jh_gc, jh.jh_ge, jh.jh_jobnum
FROM JobHeader jh
WHERE jh.jh_parentid = @anchorPk
    AND jh.jh_parenttablecode = '{scope.ParentTableCode}'
ORDER BY jh.jh_isvalid DESC, jh.jh_pk
", jobDp);

            if (job != null && !string.IsNullOrWhiteSpace(job.jh_pk))
                return job;

            if (!scope.IsConsol)
                throw new Exception($"未找到该{scope.DisplayName}对应的作业头。");

            // ---- 合单：按需创建作业头 ----
            // 模板行优先取该合单挂的运单的作业头（分公司/公司/部门与实际业务一致），否则任意一条。
            var tplDp = new DynamicParameters();
            AddAnchorPk(tplDp, anchorPk);
            var templatePk = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 jh.jh_pk
FROM JobHeader jh
WHERE jh.jh_parenttablecode = 'JS'
    AND jh.jh_parentid IN (SELECT l.jn_js FROM JobConShipLink l WHERE l.jn_jk = @anchorPk)
ORDER BY jh.jh_isvalid DESC, jh.jh_pk", tplDp)
                ?? await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                    "SELECT TOP 1 jh_pk FROM JobHeader ORDER BY jh_pk", new DynamicParameters());

            if (string.IsNullOrWhiteSpace(templatePk))
                throw new Exception("No JobHeader template row available to satisfy NOT NULL columns.");

            // jh_headertype：库里已有合单作业头时沿用它的取值，否则沿用模板行（CargoWise 若对合单
            // 作业头有专门的 headertype，这里是唯一需要调整的地方）。
            var consolHeaderType = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 jh_headertype FROM JobHeader WHERE jh_parenttablecode = 'JK'",
                new DynamicParameters());

            var anchorDp = new DynamicParameters();
            AddAnchorPk(anchorDp, anchorPk);
            var consol = await _appSqlServerRepository.QueryFirstOrDefaultAsync<ConsolRefRow>($@"
SELECT TOP 1 {scope.ConsignRefColumn} AS consign_ref, jk_transportmode AS transport_mode
FROM {scope.ParentTable}
WHERE {scope.ParentPkColumn} = @anchorPk", anchorDp);

            var newJhPk = Guid.NewGuid().ToString();
            var p = new DynamicParameters();
            p.Add("pk", newJhPk);
            AddAnchorPk(p, anchorPk);
            p.Add("parentTableCode", scope.ParentTableCode);
            p.Add("jobnum", consol?.consign_ref);
            p.Add("headerType", string.IsNullOrWhiteSpace(consolHeaderType) ? null : consolHeaderType);
            p.Add("transportMode", string.IsNullOrWhiteSpace(consol?.transport_mode) ? null : consol.transport_mode);
            p.Add("now", DateTime.UtcNow);
            p.Add("user", SysUser);
            p.Add("templatePk", templatePk);
            await _appSqlServerRepository.ExecuteAsync(InsertJobHeaderSql, p);

            var created = await _appSqlServerRepository.QueryFirstOrDefaultAsync<JobHeaderCtx>(
                "SELECT jh_pk, jh_gb, jh_gc, jh_ge, jh_jobnum FROM JobHeader WHERE jh_pk = @pk",
                new DynamicParameters(new { pk = newJhPk }));

            if (created == null)
                throw new Exception($"创建{scope.DisplayName}作业头失败。");

            return created;
        }

        /// <summary>新增 / 修改 应收应付费用（JobCharge）。无 jr_pk 新增，有 jr_pk 修改。</summary>
        public async Task<BillingCreateOrUpdateOutput> CreateOrUpdateAsync(BillingScope scope, string anchorPk,
            List<BillingChargeWriteItem> charges)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(anchorPk))
                    throw new Exception($"{scope.PkParamName} cannot be empty.");
                if (charges == null || charges.Count == 0)
                    throw new Exception("charges cannot be empty.");

                // 作废判断以锚点表的 iscancelled 为准（不是 JobHeader.jh_isvalid）
                var anchorDp = new DynamicParameters();
                AddAnchorPk(anchorDp, anchorPk);
                var cancelled = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int?>(
                    $"SELECT {scope.CancelledColumn} FROM {scope.ParentTable} WHERE {scope.ParentPkColumn} = @anchorPk",
                    anchorDp);
                if (cancelled == null)
                    throw new Exception($"{scope.DisplayName}不存在。");
                if (cancelled == 1)
                    throw new Exception($"该{scope.DisplayName}已取消，无法添加费用。");

                var job = await EnsureJobHeaderAsync(scope, anchorPk);

                // 用于满足 NOT NULL 列的模板行：优先取同 job 的一条 charge，否则任意一条
                var templateDp = new DynamicParameters();
                templateDp.Add("jh", job.jh_pk);
                var templateChargePk = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 jr_pk FROM JobCharge WHERE jr_jh = @jh
ORDER BY CASE WHEN jr_isvalid = 1 THEN 0 ELSE 1 END, jr_pk", templateDp)
                    ?? await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                        "SELECT TOP 1 jr_pk FROM JobCharge ORDER BY jr_pk", new DynamicParameters());

                if (string.IsNullOrWhiteSpace(templateChargePk))
                    throw new Exception("No JobCharge template row available to satisfy NOT NULL columns.");

                // 当前 job 下最大显示序号，新增行依次 +1
                var seqDp = new DynamicParameters();
                seqDp.Add("jh", job.jh_pk);
                var maxSeq = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int?>(
                    "SELECT MAX(jr_displaysequence) FROM JobCharge WHERE jr_jh = @jh", seqDp) ?? 0;

                var now = DateTime.UtcNow;
                var changeLogs = new List<ChargeChangeLog>();

                foreach (var c in charges)
                {
                    var isAr = string.Equals(c.chargeType, "AR", StringComparison.OrdinalIgnoreCase);
                    var isAp = string.Equals(c.chargeType, "AP", StringComparison.OrdinalIgnoreCase);
                    if (!isAr && !isAp)
                        throw new Exception("chargeType must be AR or AP.");

                    // 所选侧业务值；另一侧置 0/NULL
                    var party = c.party_oh;
                    var ccy = c.currency;
                    var rate = c.exchange_rate ?? 0m;
                    var os = c.os_amount ?? 0m;
                    var local = c.amount ?? 0m;
                    // gst/wht/vat：前端从各自数据源下拉选择后回传 pk(uniqueidentifier 外键)，直接存。
                    // 传空 / 非合法 GUID → 存 NULL(不报错)。
                    Guid? gst = Guid.TryParse(c.gst_rate, out var gstG) ? gstG : (Guid?)null;
                    Guid? wht = Guid.TryParse(c.wht_rate, out var whtG) ? whtG : (Guid?)null;
                    Guid? vat = Guid.TryParse(c.vat_class, out var vatG) ? vatG : (Guid?)null;
                    // 费用代码：前端传 pk(ac_pk) 存 jr_ac；并按 pk 查出分类码回填 jr_chargetype。
                    Guid? ac = Guid.TryParse(c.jr_ac, out var acG) ? acG : (Guid?)null;
                    string chargeTypeCode = c.jr_chargetype;
                    if (ac != null)
                    {
                        var acDp = new DynamicParameters();
                        acDp.Add("ac", ac);
                        chargeTypeCode = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                            "SELECT TOP 1 ac_chargetype FROM AccChargeCode WHERE ac_pk = @ac", acDp);
                    }
                    // AP 按负数存储（与 snt 现有 AP 数据一致）
                    if (isAp)
                    {
                        os = -Math.Abs(os);
                        local = -Math.Abs(local);
                    }

                    var p = new DynamicParameters();
                    p.Add("ac", ac);
                    p.Add("code", chargeTypeCode);
                    p.Add("desc", c.jr_desc);
                    p.Add("invoiceType", c.jr_invoicetype);
                    p.Add("now", now);
                    p.Add("user", SysUser);
                    // 双侧参数
                    p.Add("sellParty", isAr ? party : null);
                    p.Add("sellCcy", isAr ? ccy : null);
                    p.Add("sellRate", isAr ? rate : 0m);
                    p.Add("osSell", isAr ? os : 0m);
                    p.Add("localSell", isAr ? local : 0m);
                    p.Add("sellGst", isAr ? gst : null);
                    p.Add("sellWht", isAr ? wht : null);
                    p.Add("sellVat", isAr ? vat : null);
                    p.Add("costParty", isAp ? party : null);
                    p.Add("costCcy", isAp ? ccy : null);
                    p.Add("costRate", isAp ? rate : 0m);
                    p.Add("osCost", isAp ? os : 0m);
                    p.Add("localCost", isAp ? local : 0m);
                    p.Add("costGst", isAp ? gst : null);
                    p.Add("costWht", isAp ? wht : null);
                    p.Add("costVat", isAp ? vat : null);

                    if (string.IsNullOrWhiteSpace(c.jr_pk))
                    {
                        // ---- 新增：复制模板行 + 覆盖业务列 ----
                        var newPk = Guid.NewGuid().ToString();
                        p.Add("pk", newPk);
                        p.Add("jh", job.jh_pk);
                        // 分支：入参优先，为空则继承 JobHeader.jh_gb
                        p.Add("gb", string.IsNullOrWhiteSpace(c.jr_gb) ? job.jh_gb : c.jr_gb);
                        p.Add("gc", job.jh_gc);
                        p.Add("ge", job.jh_ge);
                        p.Add("ledger", isAr ? "AR" : "AP");
                        p.Add("seq", ++maxSeq);
                        p.Add("templatePk", templateChargePk);

                        await _appSqlServerRepository.ExecuteAsync(InsertJobChargeSql, p);

                        changeLogs.Add(new ChargeChangeLog { Pk = newPk, Action = "Create" });
                    }
                    else
                    {
                        // ---- 修改：所选侧已开票/过账(jr_al_*line 有值)则跳过 ----
                        var lockDp = new DynamicParameters();
                        lockDp.Add("pk", c.jr_pk);
                        var lockCol = isAr ? "jr_al_arline" : "jr_al_apline";
                        var linked = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                            $"SELECT {lockCol} FROM JobCharge WHERE jr_pk = @pk", lockDp);
                        if (!string.IsNullOrWhiteSpace(linked))
                            continue; // 已锁定，跳过

                        p.Add("pk", c.jr_pk);
                        // 分支：入参为空则沿用原值
                        p.Add("gb", string.IsNullOrWhiteSpace(c.jr_gb) ? null : c.jr_gb);
                        var setSide = isAr
                            ? @"jr_oh_sellaccount=@sellParty, jr_rx_nksellcurrency=@sellCcy, jr_ossellexrate=@sellRate,
                            jr_ossellamt=@osSell, jr_localsellamt=@localSell, jr_at_sellgstrate=@sellGst,
                            jr_aw_sellwhtrate=@sellWht, jr_a9_sellvatclass=@sellVat"
                            : @"jr_oh_costaccount=@costParty, jr_rx_nkcostcurrency=@costCcy, jr_oscostexrate=@costRate,
                            jr_oscostamt=@osCost, jr_localcostamt=@localCost, jr_at_costgstrate=@costGst,
                            jr_aw_costwhtrate=@costWht, jr_a9_costvatclass=@costVat";

                        var rows = await _appSqlServerRepository.ExecuteAsync($@"
UPDATE JobCharge SET
    jr_ac = COALESCE(@ac, jr_ac),
    jr_chargetype = COALESCE(@code, jr_chargetype),
    jr_desc = @desc,
    jr_gb = COALESCE(@gb, jr_gb),
    jr_invoicetype = COALESCE(@invoiceType, jr_invoicetype),
    {setSide},
    jr_systemlastedittimeutc = @now,
    jr_systemlastedituser = @user
WHERE jr_pk = @pk", p);

                        if (rows > 0)
                            changeLogs.Add(new ChargeChangeLog { Pk = c.jr_pk, Action = "Update" });
                    }
                }

                return new BillingCreateOrUpdateOutput { ChangeLogs = changeLogs };
            }
            catch (Exception ex)
            {
                DumpError("CreateOrUpdate", ex);
                throw;
            }
        }

        /// <summary>生成草稿发票，返回新建的发票号列表。</summary>
        public async Task<List<string>> GenerateDraftAsync(BillingScope scope, List<string> pks, string chargeType)
        {
            var created = await BuildInvoicesAsync(scope, pks, chargeType);
            return created.Select(x => x.invNo).ToList();
        }

        /// <summary>
        /// 按 结算单位+币种 分组，为选中的 JobCharge 生成草稿 AccTransactionHeader + Lines
        /// （ah_postdate = NULL，状态 draft），并回填 jr_al_arline / jr_al_apline。
        /// GenerateDraft 与 PostCharge 共用；返回新建的 (发票头 ah_pk, 发票号) 列表。
        /// </summary>
        private async Task<List<(string ahPk, string invNo)>> BuildInvoicesAsync(BillingScope scope,
            List<string> pks, string chargeType)
        {
            if (pks == null || pks.Count == 0)
                throw new Exception("pks cannot be empty.");
            var isAr = string.Equals(chargeType, "AR", StringComparison.OrdinalIgnoreCase);
            var isAp = string.Equals(chargeType, "AP", StringComparison.OrdinalIgnoreCase);
            if (!isAr && !isAp)
                throw new Exception("chargeType must be AR or AP.");

            var sideAmt = isAr ? "jr.jr_localsellamt" : "jr.jr_localcostamt";
            var sideOs = isAr ? "jr.jr_ossellamt" : "jr.jr_oscostamt";
            var sideCcy = isAr ? "jr.jr_rx_nksellcurrency" : "jr.jr_rx_nkcostcurrency";
            var sideParty = isAr ? "jr.jr_oh_sellaccount" : "jr.jr_oh_costaccount";
            var sideRate = isAr ? "jr.jr_ossellexrate" : "jr.jr_oscostexrate";
            var sideGst = isAr ? "jr.jr_at_sellgstrate" : "jr.jr_at_costgstrate";
            var sideWht = isAr ? "jr.jr_aw_sellwhtrate" : "jr.jr_aw_costwhtrate";
            var sideVat = isAr ? "jr.jr_a9_sellvatclass" : "jr.jr_a9_costvatclass";
            var sideLink = isAr ? "jr.jr_al_arline" : "jr.jr_al_apline";

            // 加载选中且该侧尚未开票(link IS NULL)的费用 + 所属作业/锚点上下文
            var loadDp = new DynamicParameters();
            loadDp.Add("pks", pks);
            var charges = (await _appSqlServerRepository.QueryAsync<ChargeDraftRow>($@"
SELECT
    jr.jr_pk        AS jr_pk,
    jr.jr_jh        AS jr_jh,
    jr.jr_chargetype AS jr_chargetype,
    jr.jr_desc      AS jr_desc,
    jr.jr_displaysequence AS jr_displaysequence,
    {sideParty}     AS party_oh,
    {sideCcy}       AS currency,
    {sideRate}      AS exchange_rate,
    {sideOs}        AS os_amount,
    {sideAmt}       AS amount,
    {sideGst}       AS gst_rate,
    {sideWht}       AS wht_rate,
    {sideVat}       AS vat_class,
    jh.jh_pk        AS jh_pk,
    jh.jh_parentid  AS anchor_pk,
    jh.jh_jobnum    AS jh_jobnum,
    jh.jh_gb        AS jh_gb,
    jh.jh_gc        AS jh_gc,
    jh.jh_ge        AS jh_ge
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
WHERE jr.jr_pk IN @pks
    AND jh.jh_parenttablecode = '{scope.ParentTableCode}'
    AND {sideLink} IS NULL
", loadDp)).ToList();

            if (charges.Count == 0)
                return new List<(string ahPk, string invNo)>();

            var anchorPk = charges[0].anchor_pk;

            var (consignRef, maxIndex) = await InvoiceSeriesAsync(scope, anchorPk, charges[0].jh_jobnum);

            // 模板行（满足 NOT NULL）
            var templateHeaderPk = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 ah_pk FROM AccTransactionHeader WHERE ah_transactiontype = 'INV' ORDER BY ah_pk",
                new DynamicParameters());
            var templateLinePk = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 al_pk FROM AccTransactionLines ORDER BY al_pk", new DynamicParameters());
            if (string.IsNullOrWhiteSpace(templateHeaderPk) || string.IsNullOrWhiteSpace(templateLinePk))
                throw new Exception("No AccTransactionHeader/Lines template row available to satisfy NOT NULL columns.");

            var ledger = isAr ? "AR" : "AP";
            var altype = isAr ? "REV" : "CST";
            var sign = isAr ? 1m : -1m; // AP 按负数存储
            var now = DateTime.UtcNow;
            var invoiceDate = now.Date;
            var created = new List<(string ahPk, string invNo)>();

            // 按 结算单位 + 币种 分组，每组一个草稿发票
            var groups = charges.GroupBy(x => new { Party = x.party_oh ?? "", Ccy = x.currency ?? "" });

            foreach (var g in groups)
            {
                var items = g.OrderBy(x => x.jr_displaysequence).ToList();
                var invNo = $"{consignRef}/{GenerateInvoiceSuffix(++maxIndex)}";

                var ahPk = Guid.NewGuid().ToString();
                created.Add((ahPk, invNo));
                var first = items[0];
                var amount = sign * items.Sum(x => Math.Abs(x.amount ?? 0));
                var osTotal = sign * items.Sum(x => Math.Abs(x.os_amount ?? 0));

                var hp = new DynamicParameters();
                hp.Add("ahpk", ahPk);
                hp.Add("ledger", ledger);
                hp.Add("invno", invNo);
                hp.Add("desc", first.jr_desc);
                hp.Add("invdate", invoiceDate);
                hp.Add("amt", amount);
                hp.Add("gst", 0m);
                hp.Add("wht", 0m);
                hp.Add("ostotal", osTotal);
                hp.Add("ccy", first.currency);
                hp.Add("rate", first.exchange_rate ?? 0m);
                hp.Add("oh", string.IsNullOrWhiteSpace(g.Key.Party) ? null : g.Key.Party);
                hp.Add("jh", first.jh_pk);
                hp.Add("gb", first.jh_gb);
                hp.Add("gc", first.jh_gc);
                hp.Add("ge", first.jh_ge);
                hp.Add("jobnum", first.jh_jobnum);
                hp.Add("now", now);
                hp.Add("user", SysUser);
                hp.Add("templatePk", templateHeaderPk);
                await _appSqlServerRepository.ExecuteAsync(InsertDraftHeaderSql, hp);

                var seq = 0;
                foreach (var c in items)
                {
                    var alPk = Guid.NewGuid().ToString();
                    var lp = new DynamicParameters();
                    lp.Add("alpk", alPk);
                    lp.Add("altype", altype);
                    lp.Add("seq", ++seq);
                    lp.Add("desc", c.jr_desc);
                    lp.Add("localamt", sign * Math.Abs(c.amount ?? 0));
                    lp.Add("gst", 0m);
                    lp.Add("vat", c.vat_class);
                    lp.Add("gstcode", c.gst_rate);
                    lp.Add("whtcode", c.wht_rate);
                    lp.Add("osamt", sign * Math.Abs(c.os_amount ?? 0));
                    lp.Add("ccy", c.currency);
                    lp.Add("rate", c.exchange_rate ?? 0m);
                    lp.Add("jh", c.jh_pk);
                    lp.Add("oh", string.IsNullOrWhiteSpace(c.party_oh) ? null : c.party_oh);
                    lp.Add("gb", c.jh_gb);
                    lp.Add("gc", c.jh_gc);
                    lp.Add("ge", c.jh_ge);
                    lp.Add("ah", ahPk);
                    lp.Add("now", now);
                    lp.Add("user", SysUser);
                    lp.Add("templatePk", templateLinePk);
                    await _appSqlServerRepository.ExecuteAsync(InsertDraftLineSql, lp);

                    // 回填 JobCharge 该侧的已开票链接
                    var linkDp = new DynamicParameters();
                    linkDp.Add("alpk", alPk);
                    linkDp.Add("jrpk", c.jr_pk);
                    var linkCol = isAr ? "jr_al_arline" : "jr_al_apline";
                    // 不写 jr_*linepostingstatus：该列为可空 varchar(3)，'draft'/'posted' 超长且 snt 现有数据不使用；
                    // 状态由 jr_al_arline(是否已开票) + AccTransactionHeader.ah_postdate(是否已过账) 推导
                    await _appSqlServerRepository.ExecuteAsync(
                        $"UPDATE JobCharge SET {linkCol} = @alpk WHERE jr_pk = @jrpk", linkDp);
                }
            }

            return created;
        }

        /// <summary>直接过账：建发票头/行后立即过账，返回过账成功的发票头数量。</summary>
        public async Task<int> PostChargeAsync(BillingScope scope, List<string> pks, string chargeType)
        {
            // 直接过账：选中的 JobCharge 先建发票头/行（复用草稿建头逻辑），再立即过账。
            // 不再要求前端预先生成草稿并传入 ah_pk。
            var created = await BuildInvoicesAsync(scope, pks, chargeType);
            if (created.Count == 0)
                return 0;

            var isAr = string.Equals(chargeType, "AR", StringComparison.OrdinalIgnoreCase);
            var linkCol = isAr ? "jr_al_arline" : "jr_al_apline";

            var now = DateTime.UtcNow;
            var posted = 0;

            foreach (var (ahPk, _) in created)
            {
                if (string.IsNullOrWhiteSpace(ahPk)) continue;

                var upDp = new DynamicParameters();
                upDp.Add("ah", ahPk);
                upDp.Add("now", now);

                // 1. 发票头过账
                await _appSqlServerRepository.ExecuteAsync(@"
UPDATE AccTransactionHeader
SET ah_postdate = @now, ah_invoiceapproved = 1, ah_systemlastedittimeutc = @now
WHERE ah_pk = @ah AND ah_postdate IS NULL", upDp);

                // 2. 发票行过账
                await _appSqlServerRepository.ExecuteAsync(
                    "UPDATE AccTransactionLines SET al_postdate = @now WHERE al_ah = @ah", upDp);

                // 3. 触碰关联 JobCharge 的最后编辑时间（不写 jr_*linepostingstatus：见 BuildInvoicesAsync 说明）
                await _appSqlServerRepository.ExecuteAsync($@"
UPDATE JobCharge SET jr_systemlastedittimeutc = @now
WHERE {linkCol} IN (SELECT al_pk FROM AccTransactionLines WHERE al_ah = @ah)", upDp);

                posted++;
            }

            return posted;
        }

        /// <summary>批量删除费用（仅两侧都未开票的行做逻辑删除 jr_isvalid=0）。</summary>
        public async Task<int> DeleteAsync(List<string> jrPks)
        {
            if (jrPks == null || jrPks.Count == 0)
                throw new Exception("jrPks cannot be empty.");

            // 仅删除两侧都未开票(jr_al_arline/jr_al_apline 均空)的费用；已开票/已过账的需先作废发票
            var dp = new DynamicParameters();
            dp.Add("pks", jrPks.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList());
            dp.Add("now", DateTime.UtcNow);
            return await _appSqlServerRepository.ExecuteAsync(@"
UPDATE JobCharge
SET jr_isvalid = 0, jr_systemlastedittimeutc = @now
WHERE jr_pk IN @pks
    AND jr_al_arline IS NULL
    AND jr_al_apline IS NULL", dp);
        }

        /// <summary>
        /// 作废草稿发票（未过账，ah_postdate IS NULL）：发票头 ah_iscancelled=1，并解锁关联 JobCharge。
        /// 草稿从未进过总账，不生成 CRD 冲销单。
        /// </summary>
        public async Task<int> VoidDraftInvoiceAsync(List<string> ahPks)
        {
            if (ahPks == null || ahPks.Count == 0)
                throw new Exception("ahPks cannot be empty.");

            var now = DateTime.UtcNow;
            var affected = 0;

            foreach (var ahPk in ahPks.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                var hDp = new DynamicParameters();
                hDp.Add("ah", ahPk);
                var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<HeaderPostRow>(
                    "SELECT ah_pk, ah_ledger, ah_postdate FROM AccTransactionHeader WHERE ah_pk = @ah AND ah_iscancelled = 0", hDp);
                if (header == null) continue;
                if (header.ah_postdate != null)
                    throw new Exception($"Cannot void posted invoice as draft: {ahPk}. Use VoidPostedInvoice instead.");

                await UnlinkChargesByHeaderAsync(ahPk, header.ah_ledger, now);

                var cDp = new DynamicParameters();
                cDp.Add("ah", ahPk);
                cDp.Add("now", now);
                await _appSqlServerRepository.ExecuteAsync(
                    "UPDATE AccTransactionHeader SET ah_iscancelled = 1, ah_systemlastedittimeutc = @now WHERE ah_pk = @ah", cDp);
                affected++;
            }

            return affected;
        }

        /// <summary>
        /// 作废正式账单（已过账）：有核销/付款的不允许作废。
        /// 作废动作按 snt 库内既有 CRD（9008 条）的惯例走一整套冲销流程，见 <see cref="BuildCreditNoteSql"/>。
        /// 入参为发票号 ah_transactionnum 列表，返回作废数量。
        /// </summary>
        /// <param name="invoiceNos">发票号 ah_transactionnum 列表</param>
        /// <param name="reason">3 字原因码（库内取值如 WOR/IAM/IDE），写入 AccTransactionMatchLink.ap_reason 与冲销说明；可空</param>
        /// <param name="reasonDesc">原因说明文字，写入冲销说明；可空</param>
        public async Task<int> VoidPostedInvoiceAsync(List<string> invoiceNos, string reason = null, string reasonDesc = null)
        {
            if (invoiceNos == null || invoiceNos.Count == 0)
                throw new Exception("invoiceNos cannot be empty.");

            // ap_reason / ah_systemcreateuser 等列都是 varchar(3)，超长会直接插入失败
            var reasonCode = Truncate((reason ?? "").Trim().ToUpperInvariant(), 3);
            var nowUtc = DateTime.UtcNow;
            var nowLocal = DateTime.Now;   // 业务日期列（ap_matchdate / ah_fullypaiddate）库内存的是本地时间
            var affected = 0;

            foreach (var no in invoiceNos.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct())
            {
                var hDp = new DynamicParameters();
                hDp.Add("no", no);
                var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<HeaderVoidRow>(@"
SELECT ah_pk, ah_ledger, ah_postdate, ah_outstandingamount, ah_invoiceamount, ah_jh
FROM AccTransactionHeader
WHERE ah_transactionnum = @no AND ah_iscancelled = 0 AND ah_postdate IS NOT NULL", hDp);
                if (header == null) continue;

                // 已有核销/付款（未结金额 != 发票金额）不允许作废
                if (Math.Abs(header.ah_outstandingamount) < Math.Abs(header.ah_invoiceamount))
                    throw new Exception($"Cannot void matched/paid invoice: {no}.");

                var isAr = string.Equals(header.ah_ledger, "AR", StringComparison.OrdinalIgnoreCase);

                // 冲销单号：AR 取本锚点序列的下一个号，AP 沿用原号（与库内既有 CRD 的做法一致：
                // AR 号是自家开的连续号，红字要另取；AP 号是对方给的供应商发票号，撤销时沿用）
                var crdNo = no;
                if (isAr)
                {
                    var anchor = await ResolveAnchorAsync(header.ah_pk);
                    if (anchor.scope != null)
                    {
                        var (consignRef, maxIndex) = await InvoiceSeriesAsync(anchor.scope, anchor.anchorPk, null);
                        crdNo = $"{consignRef}/{GenerateInvoiceSuffix(maxIndex + 1)}";
                    }
                    // 解析不到锚点（ah_jh 为空）时回落同号，不阻断作废
                }

                // 冲销说明：照库内格式「撤销相关于 <原单号> - <原因码> - <说明> 输入者 <用户>」
                var descParts = new List<string> { $"撤销相关于 {no}" };
                if (!string.IsNullOrWhiteSpace(reasonCode)) descParts.Add(reasonCode);
                if (!string.IsNullOrWhiteSpace(reasonDesc)) descParts.Add(reasonDesc.Trim());
                var crdDesc = Truncate($"{string.Join(" - ", descParts)} 输入者 {SysUser}", 200);

                // MatchGroupNum 是 varchar(20)。库内 CargoWise 自己的编号是 M+8 位数字（M00075287），
                // 这里用 M+15 位时间戳，保证永远不会和它的序列撞号。
                var matchGroup = "M" + nowLocal.ToString("yyMMddHHmmssfff");

                // 建 CRD + 对冲 + 清零 + 作废 + 解锁费用，一个批处理内完成（BEGIN TRAN 保证原子性）
                var crdPk = Guid.NewGuid().ToString();
                var p = new DynamicParameters();
                p.Add("origPk", header.ah_pk);
                p.Add("crdPk", crdPk);
                p.Add("crdNo", crdNo);
                p.Add("crdDesc", crdDesc);
                p.Add("matchGroup", matchGroup);
                p.Add("reason", reasonCode);
                p.Add("nowUtc", nowUtc);
                p.Add("nowLocal", nowLocal);
                p.Add("user", SysUser);
                await _appSqlServerRepository.ExecuteAsync(BuildCreditNoteSql(isAr), p);

                Console.WriteLine($"[VoidPostedInvoice] {no} cancelled; CRD {crdNo} (ah_pk={crdPk}) created and matched off, group={matchGroup}");
                affected++;
            }

            return affected;
        }

        private static string Truncate(string value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

        /// <summary>
        /// 按发票头反解它挂的锚点（运单 / 合单）。ah_jh 为空或作业头缺失时返回 (null, null)。
        /// </summary>
        private async Task<(BillingScope scope, string anchorPk)> ResolveAnchorAsync(string ahPk)
        {
            var row = await _appSqlServerRepository.QueryFirstOrDefaultAsync<AnchorRow>(@"
SELECT TOP 1 jh.jh_parentid AS anchor_pk, jh.jh_parenttablecode AS parent_table_code
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
WHERE ah.ah_pk = @ah", new DynamicParameters(new { ah = ahPk }));

            if (row == null || string.IsNullOrWhiteSpace(row.anchor_pk))
                return (null, null);

            return (BillingScope.FromParentTableCode(row.parent_table_code), row.anchor_pk);
        }

        /// <summary>编辑草稿发票：在某张未过账发票上 删除 / 修改 / 新增 费用，并同步发票行与发票头汇总。</summary>
        public async Task<int> EditDraftInvoiceAsync(DraftInvoiceEditInput input)
        {
            if (string.IsNullOrWhiteSpace(input?.ahPk))
                throw new Exception("ahPk cannot be empty.");

            var hDp = new DynamicParameters();
            hDp.Add("ah", input.ahPk);
            var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<HeaderEditRow>(@"
SELECT ah_pk, ah_ledger, ah_postdate, ah_jh, ah_gb, ah_gc, ah_ge, ah_jobnumber
FROM AccTransactionHeader
WHERE ah_pk = @ah AND ah_iscancelled = 0", hDp);
            if (header == null)
                throw new Exception("Draft invoice not found.");
            if (header.ah_postdate != null)
                throw new Exception("Cannot edit posted invoice.");

            var isAr = string.Equals(header.ah_ledger, "AR", StringComparison.OrdinalIgnoreCase);
            var sign = isAr ? 1m : -1m;
            var linkCol = isAr ? "jr_al_arline" : "jr_al_apline";
            var statusCol = isAr ? "jr_arlinepostingstatus" : "jr_aplinepostingstatus";
            var now = DateTime.UtcNow;
            var affected = 0;

            // ====== 删除：从发票移除指定费用（删行 + 解锁 charge） ======
            if (input.deleteJrPks != null)
            {
                foreach (var jrPk in input.deleteJrPks.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                {
                    var dDp = new DynamicParameters();
                    dDp.Add("jr", jrPk);
                    dDp.Add("ah", input.ahPk);
                    dDp.Add("now", now);
                    // 删除该 charge 在本发票下的行
                    await _appSqlServerRepository.ExecuteAsync($@"
DELETE FROM AccTransactionLines
WHERE al_ah = @ah AND al_pk = (SELECT {linkCol} FROM JobCharge WHERE jr_pk = @jr)", dDp);
                    // 解锁 charge
                    await _appSqlServerRepository.ExecuteAsync(
                        $"UPDATE JobCharge SET {linkCol} = NULL, {statusCol} = NULL, jr_systemlastedittimeutc = @now WHERE jr_pk = @jr", dDp);
                    affected++;
                }
            }

            // ====== 新增 / 修改 ======
            if (input.charges != null && input.charges.Count > 0)
            {
                var templateChargePk = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                    "SELECT TOP 1 jr_pk FROM JobCharge WHERE jr_jh = @jh ORDER BY jr_pk",
                    new DynamicParameters(new { jh = header.ah_jh }))
                    ?? await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                        "SELECT TOP 1 jr_pk FROM JobCharge ORDER BY jr_pk", new DynamicParameters());
                var templateLinePk = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                    "SELECT TOP 1 al_pk FROM AccTransactionLines ORDER BY al_pk", new DynamicParameters());

                var maxSeq = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int?>(
                    "SELECT MAX(al_sequence) FROM AccTransactionLines WHERE al_ah = @ah",
                    new DynamicParameters(new { ah = input.ahPk })) ?? 0;

                foreach (var c in input.charges)
                {
                    var os = sign * Math.Abs(c.os_amount ?? 0);
                    var local = sign * Math.Abs(c.amount ?? 0);

                    if (string.IsNullOrWhiteSpace(c.jr_pk))
                    {
                        // ---- 新增 charge + 发票行，并回填链接 ----
                        if (string.IsNullOrWhiteSpace(templateChargePk) || string.IsNullOrWhiteSpace(templateLinePk))
                            throw new Exception("No template row available to satisfy NOT NULL columns.");

                        var jrPk = Guid.NewGuid().ToString();
                        var alPk = Guid.NewGuid().ToString();

                        // JobCharge（按 ledger 写所选侧；金额取绝对值，read 投影按正数）
                        var jp = BuildSideChargeParams(isAr, c, jrPk, header.ah_jh, header.ah_gb, header.ah_gc, header.ah_ge, ++maxSeq, now, templateChargePk);
                        await _appSqlServerRepository.ExecuteAsync(InsertJobChargeSql, jp);

                        // 发票行
                        var lp = new DynamicParameters();
                        lp.Add("alpk", alPk);
                        lp.Add("altype", isAr ? "REV" : "CST");
                        lp.Add("seq", maxSeq);
                        lp.Add("desc", c.jr_desc);
                        lp.Add("localamt", local);
                        lp.Add("gst", 0m);
                        lp.Add("vat", c.vat_class);
                        lp.Add("gstcode", c.gst_rate);
                        lp.Add("whtcode", c.wht_rate);
                        lp.Add("osamt", os);
                        lp.Add("ccy", c.currency);
                        lp.Add("rate", c.exchange_rate ?? 0m);
                        lp.Add("jh", header.ah_jh);
                        lp.Add("oh", string.IsNullOrWhiteSpace(c.party_oh) ? null : c.party_oh);
                        lp.Add("gb", header.ah_gb);
                        lp.Add("gc", header.ah_gc);
                        lp.Add("ge", header.ah_ge);
                        lp.Add("ah", input.ahPk);
                        lp.Add("now", now);
                        lp.Add("user", SysUser);
                        lp.Add("templatePk", templateLinePk);
                        await _appSqlServerRepository.ExecuteAsync(InsertDraftLineSql, lp);

                        // 回填链接（不写 jr_*linepostingstatus：见 BuildInvoicesAsync 说明）
                        await _appSqlServerRepository.ExecuteAsync(
                            $"UPDATE JobCharge SET {linkCol} = @al WHERE jr_pk = @jr",
                            new DynamicParameters(new { al = alPk, jr = jrPk }));
                        affected++;
                    }
                    else
                    {
                        // ---- 修改：同步 JobCharge 所选侧 + 对应发票行 ----
                        var up = new DynamicParameters();
                        up.Add("jr", c.jr_pk);
                        up.Add("code", c.jr_chargetype);
                        up.Add("desc", c.jr_desc);
                        up.Add("party", string.IsNullOrWhiteSpace(c.party_oh) ? null : c.party_oh);
                        up.Add("ccy", c.currency);
                        up.Add("rate", c.exchange_rate ?? 0m);
                        up.Add("os", os);
                        up.Add("local", local);
                        up.Add("gst", c.gst_rate);
                        up.Add("wht", c.wht_rate);
                        up.Add("vat", c.vat_class);
                        up.Add("now", now);
                        up.Add("user", SysUser);

                        var setSide = isAr
                            ? @"jr_oh_sellaccount=@party, jr_rx_nksellcurrency=@ccy, jr_ossellexrate=@rate,
                                jr_ossellamt=@os, jr_localsellamt=@local, jr_at_sellgstrate=@gst,
                                jr_aw_sellwhtrate=@wht, jr_a9_sellvatclass=@vat"
                            : @"jr_oh_costaccount=@party, jr_rx_nkcostcurrency=@ccy, jr_oscostexrate=@rate,
                                jr_oscostamt=@os, jr_localcostamt=@local, jr_at_costgstrate=@gst,
                                jr_aw_costwhtrate=@wht, jr_a9_costvatclass=@vat";

                        await _appSqlServerRepository.ExecuteAsync($@"
UPDATE JobCharge SET jr_chargetype=@code, jr_desc=@desc, {setSide},
    jr_systemlastedittimeutc=@now, jr_systemlastedituser=@user
WHERE jr_pk=@jr", up);

                        // 同步对应发票行（通过 charge 的链接定位）
                        await _appSqlServerRepository.ExecuteAsync($@"
UPDATE AccTransactionLines SET
    al_desc=@desc, al_lineamount=@local, al_osamount=@os, al_unitprice=@os, al_osunitprice=@os,
    al_rx_nktransactioncurrency=@ccy, al_exchangerate=@rate, al_a9_vatclass=@vat, al_at=@gst, al_aw=@wht,
    al_systemlastedittimeutc=@now, al_systemlastedituser=@user
WHERE al_ah=@ah AND al_pk = (SELECT {linkCol} FROM JobCharge WHERE jr_pk=@jr)",
                            new DynamicParameters(new { ah = input.ahPk, jr = c.jr_pk, desc = c.jr_desc, local, os, ccy = c.currency, rate = c.exchange_rate ?? 0m, vat = c.vat_class, gst = c.gst_rate, wht = c.wht_rate, now, user = SysUser }));
                        affected++;
                    }
                }
            }

            // ====== 重算发票头汇总 ======
            await _appSqlServerRepository.ExecuteAsync(@"
UPDATE AccTransactionHeader SET
    ah_invoiceamount = ISNULL((SELECT SUM(al_lineamount) FROM AccTransactionLines WHERE al_ah = @ah), 0),
    ah_ostotal = ISNULL((SELECT SUM(al_osamount) FROM AccTransactionLines WHERE al_ah = @ah), 0),
    ah_localtotal = ISNULL((SELECT SUM(al_lineamount) FROM AccTransactionLines WHERE al_ah = @ah), 0),
    ah_outstandingamount = ISNULL((SELECT SUM(al_lineamount) FROM AccTransactionLines WHERE al_ah = @ah), 0),
    ah_osoutstandingamount = ISNULL((SELECT SUM(al_osamount) FROM AccTransactionLines WHERE al_ah = @ah), 0),
    ah_systemlastedittimeutc = @now
WHERE ah_pk = @ah", new DynamicParameters(new { ah = input.ahPk, now }));

            return affected;
        }

        /// <summary>解锁某发票头下所有关联 JobCharge：清空该侧 jr_al_*line 与过账状态</summary>
        private async Task UnlinkChargesByHeaderAsync(string ahPk, string ledger, DateTime now)
        {
            var isAr = string.Equals(ledger, "AR", StringComparison.OrdinalIgnoreCase);
            var linkCol = isAr ? "jr_al_arline" : "jr_al_apline";
            var statusCol = isAr ? "jr_arlinepostingstatus" : "jr_aplinepostingstatus";
            var dp = new DynamicParameters();
            dp.Add("ah", ahPk);
            dp.Add("now", now);
            await _appSqlServerRepository.ExecuteAsync($@"
UPDATE JobCharge SET {linkCol} = NULL, {statusCol} = '', jr_systemlastedittimeutc = @now
WHERE {linkCol} IN (SELECT al_pk FROM AccTransactionLines WHERE al_ah = @ah)", dp);
        }

        /// <summary>构造 InsertJobChargeSql 的全部参数（按 ledger 写所选侧，另一侧置 0/NULL）</summary>
        private static DynamicParameters BuildSideChargeParams(bool isAr, BillingChargeWriteItem c, string pk,
            string jh, string gb, string gc, string ge, int seq, DateTime now, string templatePk)
        {
            var rate = c.exchange_rate ?? 0m;
            var os = c.os_amount ?? 0m;
            var local = c.amount ?? 0m;
            if (!isAr) { os = -Math.Abs(os); local = -Math.Abs(local); }

            var p = new DynamicParameters();
            p.Add("pk", pk);
            p.Add("jh", jh);
            p.Add("gb", gb);
            p.Add("gc", gc);
            p.Add("ge", ge);
            p.Add("code", c.jr_chargetype);
            p.Add("desc", c.jr_desc);
            p.Add("invoiceType", c.jr_invoicetype);
            p.Add("ledger", isAr ? "AR" : "AP");
            p.Add("seq", seq);
            p.Add("now", now);
            p.Add("user", SysUser);
            p.Add("templatePk", templatePk);
            // 费用代码：非法/为空的 pk 存 NULL，由 InsertJobChargeSql 的 COALESCE 兜底到模板行
            p.Add("ac", Guid.TryParse(c.jr_ac, out var acG) ? acG : (Guid?)null);
            p.Add("sellParty", isAr ? c.party_oh : null);
            p.Add("sellCcy", isAr ? c.currency : null);
            p.Add("sellRate", isAr ? rate : 0m);
            p.Add("osSell", isAr ? os : 0m);
            p.Add("localSell", isAr ? local : 0m);
            p.Add("sellGst", isAr ? c.gst_rate : null);
            p.Add("sellWht", isAr ? c.wht_rate : null);
            p.Add("sellVat", isAr ? c.vat_class : null);
            p.Add("costParty", !isAr ? c.party_oh : null);
            p.Add("costCcy", !isAr ? c.currency : null);
            p.Add("costRate", !isAr ? rate : 0m);
            p.Add("osCost", !isAr ? os : 0m);
            p.Add("localCost", !isAr ? local : 0m);
            p.Add("costGst", !isAr ? c.gst_rate : null);
            p.Add("costWht", !isAr ? c.wht_rate : null);
            p.Add("costVat", !isAr ? c.vat_class : null);
            return p;
        }

        private static void DumpError(string tag, Exception ex)
        {
            Console.WriteLine($"=========== {tag} ERROR ===========");
            Console.WriteLine($"Type    : {ex.GetType().FullName}");
            Console.WriteLine($"Message : {ex.Message}");
            Console.WriteLine($"Stack   : {ex.StackTrace}");
            var inner = ex.InnerException;
            while (inner != null)
            {
                Console.WriteLine("--- Inner ---");
                Console.WriteLine($"Type    : {inner.GetType().FullName}");
                Console.WriteLine($"Message : {inner.Message}");
                Console.WriteLine($"Stack   : {inner.StackTrace}");
                inner = inner.InnerException;
            }
            Console.WriteLine("===================================");
        }

        // ============================================================================
        // SQL 常量
        // ============================================================================

        /// <summary>
        /// 作废已过账账单的完整冲销流程，一个批处理内完成（BEGIN TRAN 保证原子性）。
        /// 每一步都对齐 snt 库内既有 CRD 的写法（实测 9008 条）：
        ///   1) 复制原发票头 → CRD：金额类全部取反；号码 AR 取新号 / AP 沿用原号；
        ///      ah_desc 写「撤销相关于 &lt;原号&gt; - &lt;原因码&gt; - &lt;说明&gt; 输入者 &lt;用户&gt;」；
        ///      ah_originaltransactionnum 不回填（库内 9008 条无一条填过，关联靠 desc）；
        ///      CRD 自身 ah_iscancelled = 1、outstanding = 0、fullypaiddate 有值（库内 88% / 98.8% 如此）。
        ///   2) 复制原发票行 → CRD 行（金额类取反，al_pk 用 NEWID()）
        ///   3) 建两条 AccTransactionMatchLink（同一 ap_matchgroupnum，金额一正一负）把原单与 CRD 对冲
        ///   4) 原单：outstanding 清零 + fullypaiddate + ah_iscancelled = 1
        ///   5) 解锁原单关联的 JobCharge（回到"未开票"状态，可重新开票）
        /// CRD 行不回填 jr_al_*line：CRD 是纯会计冲销凭证，不占用费用。
        /// </summary>
        private static string BuildCreditNoteSql(bool isAr)
        {
            var linkCol = isAr ? "jr_al_arline" : "jr_al_apline";
            var statusCol = isAr ? "jr_arlinepostingstatus" : "jr_aplinepostingstatus";

            return $@"
SET XACT_ABORT ON;
BEGIN TRAN;

-- 1) 发票头 → CRD
INSERT INTO AccTransactionHeader (
    ah_pk, ah_ledger, ah_transactiontype, ah_compliancesubtype, ah_transactionnum,
    ah_transactioncount, ah_transactionreference, ah_desc,
    ah_invoicedate, ah_duedate, ah_invoiceamount, ah_gstamount, ah_withholdingtax,
    ah_ostotal, ah_rx_nktransactioncurrency, ah_exchangerate,
    ah_ageperiod, ah_postperiod, ah_postdate,
    ah_transactioncategory, ah_chequeorreference, ah_receipttype,
    ah_cashbasisgstindicator, ah_cashbasisgstrealisedtogl,
    ah_chequedrawer, ah_drawerbank, ah_drawerbranch,
    ah_invoiceapproved, ah_consolidatedinvoiceref, ah_fullypaiddate,
    ah_invoiceprinted, ah_iscancelled, ah_dateclearedincashbook,
    ah_notallocated, ah_outstandingamount, ah_postedtoeft, ah_posttogl,
    ah_receiptbatchno, ah_transactioncreatedbymatching,
    ah_invoiceterm, ah_invoicetermdays, ah_requisitiondate, ah_requisitionstatus,
    ah_numberofsupportingdocuments, ah_exportbatchnumber, ah_postedinternal,
    ah_post1, ah_post2, ah_post3, ah_post4,
    ah_ab, ah_oh, ah_oa_invoiceaddressoverride, ah_oc_invoicecontactoverride,
    ah_jh, ah_gb, ah_gc, ah_ge, ah_ag,
    ah_transactionbelongstogroup, ah_ah_invoicestatement,
    ah_systemcreatetimeutc, ah_systemcreateuser,
    ah_systemlastedittimeutc, ah_systemlastedituser,
    ah_agreedpaymentmethodoverride, ah_compliancedocumentdate,
    ah_gs_nkauditedby, ah_gs_nkcashier, ah_invoicepaymentreferencecode,
    ah_localtaxamountothertaxes, ah_ostaxamountothertaxes, ah_autoversion,
    ah_documentreceiveddate, ah_matchstatus, ah_matchstatusreasoncode,
    ah_originalinvoicedate, ah_originaltransactionnum,
    ah_placeofsupply, ah_placeofsupplytype, ah_xd_compliancebook,
    ah_localtotal, ah_jobnumber,
    ah_originalreferenceenddate, ah_originalreferencestartdate,
    ah_gb_taxbranch, ah_governmentallocatedid, ah_cah_cashadvancerequestheader,
    ah_isosoutstandingamountapplicable, ah_osoutstandingamount, ah_overrideexchangerate,
    ah_systemcreatebranch, ah_systemcreatedepartment
)
SELECT
    @crdPk, t.ah_ledger, 'CRD', t.ah_compliancesubtype, @crdNo,
    t.ah_transactioncount, t.ah_transactionreference, @crdDesc,
    t.ah_invoicedate, t.ah_duedate, -t.ah_invoiceamount, -t.ah_gstamount, -t.ah_withholdingtax,
    -t.ah_ostotal, t.ah_rx_nktransactioncurrency, t.ah_exchangerate,
    t.ah_ageperiod, t.ah_postperiod, t.ah_postdate,
    t.ah_transactioncategory, t.ah_chequeorreference, t.ah_receipttype,
    t.ah_cashbasisgstindicator, t.ah_cashbasisgstrealisedtogl,
    t.ah_chequedrawer, t.ah_drawerbank, t.ah_drawerbranch,
    t.ah_invoiceapproved, t.ah_consolidatedinvoiceref, @nowLocal,
    0, 1, t.ah_dateclearedincashbook,
    t.ah_notallocated, 0, t.ah_postedtoeft, t.ah_posttogl,
    t.ah_receiptbatchno, 0,
    t.ah_invoiceterm, t.ah_invoicetermdays, t.ah_requisitiondate, t.ah_requisitionstatus,
    t.ah_numberofsupportingdocuments, 0, t.ah_postedinternal,
    t.ah_post1, t.ah_post2, t.ah_post3, t.ah_post4,
    t.ah_ab, t.ah_oh, t.ah_oa_invoiceaddressoverride, t.ah_oc_invoicecontactoverride,
    t.ah_jh, t.ah_gb, t.ah_gc, t.ah_ge, t.ah_ag,
    t.ah_transactionbelongstogroup, NULL,
    @nowUtc, @user,
    NULL, @user,
    t.ah_agreedpaymentmethodoverride, t.ah_compliancedocumentdate,
    t.ah_gs_nkauditedby, t.ah_gs_nkcashier, t.ah_invoicepaymentreferencecode,
    -t.ah_localtaxamountothertaxes, -t.ah_ostaxamountothertaxes, t.ah_autoversion,
    t.ah_documentreceiveddate, t.ah_matchstatus, t.ah_matchstatusreasoncode,
    t.ah_originalinvoicedate, t.ah_originaltransactionnum,
    t.ah_placeofsupply, t.ah_placeofsupplytype, t.ah_xd_compliancebook,
    -t.ah_localtotal, t.ah_jobnumber,
    t.ah_originalreferenceenddate, t.ah_originalreferencestartdate,
    t.ah_gb_taxbranch, NULL, NULL,
    t.ah_isosoutstandingamountapplicable, 0, t.ah_overrideexchangerate,
    t.ah_systemcreatebranch, t.ah_systemcreatedepartment
FROM AccTransactionHeader t
WHERE t.ah_pk = @origPk;

-- 2) 发票行 → CRD 行
INSERT INTO AccTransactionLines (
    al_pk, al_linetype, al_sequence, al_desc, al_lineamount, al_at, al_gstvat, al_gstvatbasis,
    al_a9_vatclass, al_aw, al_withholdingtax, al_unitqty, al_unitprice, al_osunitprice, al_osamount,
    al_rx_nktransactioncurrency, al_exchangerate, al_inputgstvatrecoverable, al_postperiod, al_postdate,
    al_posttogl, al_reverseperiod, al_reversedate, al_reversetogl, al_preventinvoiceprintgrouping,
    al_exportbatchnumber, al_exportreversebatchnumber, al_isfinalcharge, al_revrecognitiontype, al_jh,
    al_ac, al_ge, al_gb, al_ag, al_oh, al_ag_percentof, al_percentageofperiod, al_ah, al_gc,
    al_systemcreatetimeutc, al_systemcreateuser, al_systemlastedittimeutc, al_systemlastedituser,
    al_govtchargecode, al_gstvatextra, al_taxdate, al_taxextraratedenominator, al_taxextraratenumerator,
    al_taxratedenominator, al_taxratenumerator, al_autoversion, al_jbb, al_placeofsupply,
    al_placeofsupplytype, al_supplytype, al_gb_taxbranch
)
SELECT
    NEWID(), t.al_linetype, t.al_sequence, t.al_desc, -t.al_lineamount, t.al_at, -t.al_gstvat, t.al_gstvatbasis,
    t.al_a9_vatclass, t.al_aw, -t.al_withholdingtax, t.al_unitqty, -t.al_unitprice, -t.al_osunitprice, -t.al_osamount,
    t.al_rx_nktransactioncurrency, t.al_exchangerate, t.al_inputgstvatrecoverable, t.al_postperiod, t.al_postdate,
    t.al_posttogl, t.al_reverseperiod, t.al_reversedate, t.al_reversetogl, t.al_preventinvoiceprintgrouping,
    0, 0, t.al_isfinalcharge, t.al_revrecognitiontype, t.al_jh,
    t.al_ac, t.al_ge, t.al_gb, t.al_ag, t.al_oh, t.al_ag_percentof, t.al_percentageofperiod, @crdPk, t.al_gc,
    @nowUtc, @user, NULL, @user,
    t.al_govtchargecode, -t.al_gstvatextra, t.al_taxdate, t.al_taxextraratedenominator, t.al_taxextraratenumerator,
    t.al_taxratedenominator, t.al_taxratenumerator, t.al_autoversion, t.al_jbb, t.al_placeofsupply,
    t.al_placeofsupplytype, t.al_supplytype, t.al_gb_taxbranch
FROM AccTransactionLines t
WHERE t.al_ah = @origPk;

-- 3) 核销对冲：原单 + CRD 各一条 MatchLink，同一 ap_matchgroupnum，金额一正一负
--    ap_amount 取各自单据自身的金额（库内样例 M00075287 就是 +22600 / -22600 一对）
--    ap_matchdate 存本地时间、ap_system*utc 存 UTC（库内样例两者相差 8 小时）
INSERT INTO AccTransactionMatchLink (
    ap_pk, ap_amount, ap_gstrealised, ap_matchgroupnum, ap_matchperiod, ap_matchdate,
    ap_ah, ap_reason, ap_systemcreateuser, ap_systemlastedittimeutc, ap_systemlastedituser,
    ap_systemcreatetimeutc, ap_osamount
)
SELECT NEWID(), t.ah_invoiceamount, 0, @matchGroup, 0, @nowLocal,
       t.ah_pk, @reason, @user, @nowUtc, @user, @nowUtc, 0
FROM AccTransactionHeader t WHERE t.ah_pk = @origPk
UNION ALL
SELECT NEWID(), -t.ah_invoiceamount, 0, @matchGroup, 0, @nowLocal,
       @crdPk, @reason, @user, @nowUtc, @user, @nowUtc, 0
FROM AccTransactionHeader t WHERE t.ah_pk = @origPk;

-- 4) 原单：被 CRD 冲平 → outstanding 清零 + fullypaiddate，然后作废
UPDATE AccTransactionHeader
SET ah_outstandingamount = 0,
    ah_osoutstandingamount = 0,
    ah_fullypaiddate = @nowLocal,
    ah_iscancelled = 1,
    ah_systemlastedittimeutc = @nowUtc
WHERE ah_pk = @origPk;

-- 5) 解锁原单关联的 JobCharge（费用回到未开票状态，可重新开票）
UPDATE JobCharge SET {linkCol} = NULL, {statusCol} = '', jr_systemlastedittimeutc = @nowUtc
WHERE {linkCol} IN (SELECT al_pk FROM AccTransactionLines WHERE al_ah = @origPk);

COMMIT;
";
        }

        // INSERT JobHeader：按需为合单创建作业头；覆盖列用 @param，其余复制模板行 t
        private const string InsertJobHeaderSql = @"
INSERT INTO JobHeader (
    jh_pk, jh_isvalid, jh_headertype, jh_name, jh_description, jh_jobnum, jh_joblocalreference,
    jh_arinvoicereference, jh_th_nkquotenumber, jh_status, jh_profitlossreasoncode,
    jh_a_jop, jh_revenuerecognizeddate, jh_a_jcl, jh_jobplannedstartdate,
    jh_jobbufferpercentoverride, jh_isprofitshareposted, jh_localchargescfx,
    jh_oa_localchargesaddr, jh_oc_localbillingcontact, jh_oa_agentcollectaddr, jh_agentchargescfx,
    jh_localclientinvoicingstyle, jh_singleagentsinvoiceperconsol, jh_uniquejobinvoicenumber,
    jh_paymentcollectionstatus, jh_ratinghasbeenrun, jh_excludefromperiodicrating, jh_profitshareinvoice,
    jh_gb, jh_ge, jh_gc, jh_gs_nkrepsales, jh_gs_nkrepops,
    jh_parentid, jh_parenttablecode, jh_jh_parentjob,
    jh_systemcreatetimeutc, jh_systemcreateuser, jh_systemlastedittimeutc, jh_systemlastedituser,
    jh_holdreason, jh_autoversion, jh_isactive, jh_clientcontractnumber, jh_gb_taxbranch,
    jh_direction, jh_isdisbursement, jh_containermode, jh_transportmode
)
SELECT
    @pk, 1, COALESCE(@headerType, t.jh_headertype), t.jh_name, t.jh_description, @jobnum, t.jh_joblocalreference,
    t.jh_arinvoicereference, NULL, t.jh_status, t.jh_profitlossreasoncode,
    NULL, NULL, NULL, NULL,
    t.jh_jobbufferpercentoverride, 0, 0,
    NULL, NULL, NULL, 0,
    t.jh_localclientinvoicingstyle, t.jh_singleagentsinvoiceperconsol, t.jh_uniquejobinvoicenumber,
    t.jh_paymentcollectionstatus, 0, t.jh_excludefromperiodicrating, NULL,
    t.jh_gb, t.jh_ge, t.jh_gc, t.jh_gs_nkrepsales, t.jh_gs_nkrepops,
    @anchorPk, @parentTableCode, NULL,
    @now, @user, NULL, @user,
    t.jh_holdreason, t.jh_autoversion, 1, t.jh_clientcontractnumber, t.jh_gb_taxbranch,
    t.jh_direction, t.jh_isdisbursement, t.jh_containermode, COALESCE(@transportMode, t.jh_transportmode)
FROM JobHeader t
WHERE t.jh_pk = @templatePk
";

        // INSERT JobCharge：列顺序与 Po 实体一致；覆盖列用 @param，其余复制模板行 t
        private const string InsertJobChargeSql = @"
INSERT INTO JobCharge (
    jr_pk, jr_isvalid, jr_jh, jr_ge, jr_gb, jr_jh_internaljob, jr_ge_internaldept, jr_gb_internalbranch,
    jr_linecfx, jr_ac, jr_desc, jr_oh_costaccount, jr_costrated, jr_costratingoverride, jr_oscostamt,
    jr_agentdeclaredcostamt, jr_localcostamt, jr_rx_nkcostcurrency, jr_oscostexrate, jr_at_costgstrate,
    jr_oscostgstamt, jr_a9_costvatclass, jr_aw_costwhtrate, jr_oscostwhtamt, jr_estimatedcost,
    jr_aplinepostingstatus, jr_costreference, jr_apinvoicenum, jr_apinvoicedate, jr_apnumberofsupportingdocuments,
    jr_paymentdate, jr_paymenttype, jr_chequeno, jr_ak, jr_ab, jr_al_apline, jr_declaredoscostamt, jr_proformacost,
    jr_oh_sellaccount, jr_oa_sellinvoiceaddress, jr_oc_sellinvoicecontact, jr_rx_nksellcurrency, jr_ossellexrate,
    jr_ossellamt, jr_a9_sellvatclass, jr_agentdeclaredsellamt, jr_localsellamt, jr_sellrated, jr_sellratingoverride,
    jr_at_sellgstrate, jr_aw_sellwhtrate, jr_ossellwhtamt, jr_sellreference, jr_al_arline, jr_al_cfxline,
    jr_estimatedrevenue, jr_isincludedinprofitshare, jr_chargetype, jr_marginpercentage, jr_arnumberofsupportingdocuments,
    jr_invoicetype, jr_proformarevenue, jr_preventinvoiceprintgrouping, jr_displaysequence, jr_arlinepostingstatus,
    jr_orderreference, jr_op_product, jr_productquantity, jr_e6, jr_gc, jr_costgovtchargecode, jr_e6_gatewaysellheader,
    jr_jr_revenueline, jr_linetype, jr_rx_nksellinvoicecurrency, jr_sellgovtchargecode, jr_costtaxdate, jr_selltaxdate,
    jr_iscosttaxamountoverridden, jr_apdocumentreceiveddate, jr_autoversion, jr_costplaceofsupply, jr_costplaceofsupplytype,
    jr_sellplaceofsupply, jr_sellplaceofsupplytype, jr_costsupplytype, jr_sellsupplytype, jr_systemcreatetimeutc,
    jr_systemcreateuser, jr_systemlastedittimeutc, jr_systemlastedituser, jr_cal_apline, jr_cal_arline,
    jr_gb_costtaxbranch, jr_gb_selltaxbranch, jr_isapcashadvance, jr_isarcashadvance, jr_isspotcost,
    jr_costratingoverridecomment, jr_sellratingoverridecomment
)
SELECT
    -- 1-8: 主键/作业上下文
    @pk, 1, @jh, @ge, @gb, t.jr_jh_internaljob, t.jr_ge_internaldept, t.jr_gb_internalbranch,
    -- 9-15: jr_linecfx, jr_ac(费用代码pk,入参优先), jr_desc, jr_oh_costaccount, jr_costrated, jr_costratingoverride, jr_oscostamt
    t.jr_linecfx, COALESCE(@ac, t.jr_ac), @desc, @costParty, t.jr_costrated, t.jr_costratingoverride, @osCost,
    -- 16-20: agentdeclaredcost, localcost, costcurrency(NOT NULL→兜底模板), costexrate, costgstrate
    0, @localCost, COALESCE(@costCcy, t.jr_rx_nkcostcurrency), @costRate, @costGst,
    -- 21-25: oscostgstamt, costvatclass, costwhtrate, oscostwhtamt, estimatedcost
    0, @costVat, @costWht, 0, 0,
    -- 26-30: aplinepostingstatus, costreference, apinvoicenum(均 NOT NULL→模板), apinvoicedate, apnumsupportdocs
    t.jr_aplinepostingstatus, t.jr_costreference, t.jr_apinvoicenum, NULL, 0,
    -- 31-38: paymentdate, paymenttype, chequeno(NOT NULL→模板), ak, ab, al_apline, declaredoscostamt, proformacost
    NULL, t.jr_paymenttype, t.jr_chequeno, NULL, NULL, NULL, 0, t.jr_proformacost,
    -- 39-43: sellaccount, sellinvoiceaddress, sellinvoicecontact, sellcurrency(NOT NULL→兜底模板), sellexrate
    @sellParty, NULL, NULL, COALESCE(@sellCcy, t.jr_rx_nksellcurrency), @sellRate,
    -- 44-49: ossellamt, sellvatclass, agentdeclaredsell, localsellamt, sellrated, sellratingoverride
    @osSell, @sellVat, 0, @localSell, t.jr_sellrated, t.jr_sellratingoverride,
    -- 50-55: sellgstrate, sellwhtrate, ossellwhtamt, sellreference(NOT NULL→模板), al_arline, al_cfxline
    @sellGst, @sellWht, 0, t.jr_sellreference, NULL, NULL,
    -- 56-60: estimatedrevenue, isincludedinprofitshare, chargetype(NOT NULL→兜底模板), marginpct, arnumsupportdocs
    0, t.jr_isincludedinprofitshare, COALESCE(@code, t.jr_chargetype), 0, 0,
    -- 61-65: invoicetype, proformarevenue, preventinvoiceprintgrouping, displaysequence, arlinepostingstatus(NOT NULL→模板)
    COALESCE(@invoiceType, t.jr_invoicetype), t.jr_proformarevenue, 0, @seq, t.jr_arlinepostingstatus,
    -- 66-72: orderreference(NOT NULL→模板), op_product, productquantity, e6, gc, costgovtchargecode(NOT NULL→模板), e6_gatewaysellheader
    t.jr_orderreference, t.jr_op_product, t.jr_productquantity, t.jr_e6, @gc, t.jr_costgovtchargecode, t.jr_e6_gatewaysellheader,
    -- 73-78: jr_revenueline, linetype, sellinvoicecurrency(NOT NULL→兜底模板), sellgovtchargecode(NOT NULL→模板), costtaxdate, selltaxdate
    NULL, @ledger, COALESCE(@sellCcy, t.jr_rx_nksellinvoicecurrency), t.jr_sellgovtchargecode, NULL, NULL,
    -- 79-83: iscosttaxamountoverridden, apdocumentreceiveddate, autoversion, costplaceofsupply, costplaceofsupplytype
    0, NULL, t.jr_autoversion, t.jr_costplaceofsupply, t.jr_costplaceofsupplytype,
    -- 84-88: sellplaceofsupply, sellplaceofsupplytype, costsupplytype, sellsupplytype, systemcreatetimeutc
    t.jr_sellplaceofsupply, t.jr_sellplaceofsupplytype, t.jr_costsupplytype, t.jr_sellsupplytype, @now,
    -- 89-93: systemcreateuser(模板), systemlastedittimeutc, systemlastedituser(模板), cal_apline, cal_arline
    t.jr_systemcreateuser, NULL, t.jr_systemcreateuser, NULL, NULL,
    -- 94-98: gb_costtaxbranch, gb_selltaxbranch, isapcashadvance, isarcashadvance, isspotcost
    NULL, NULL, t.jr_isapcashadvance, t.jr_isarcashadvance, t.jr_isspotcost,
    -- 99-100: costratingoverridecomment, sellratingoverridecomment (均 NOT NULL→模板)
    t.jr_costratingoverridecomment, t.jr_sellratingoverridecomment
FROM JobCharge t
WHERE t.jr_pk = @templatePk
";

        // INSERT AccTransactionHeader（草稿：ah_postdate = NULL）
        private const string InsertDraftHeaderSql = @"
INSERT INTO AccTransactionHeader (
    ah_pk, ah_ledger, ah_transactiontype, ah_compliancesubtype, ah_transactionnum,
    ah_transactioncount, ah_transactionreference, ah_desc,
    ah_invoicedate, ah_duedate, ah_invoiceamount, ah_gstamount, ah_withholdingtax,
    ah_ostotal, ah_rx_nktransactioncurrency, ah_exchangerate,
    ah_ageperiod, ah_postperiod, ah_postdate,
    ah_transactioncategory, ah_chequeorreference, ah_receipttype,
    ah_cashbasisgstindicator, ah_cashbasisgstrealisedtogl,
    ah_chequedrawer, ah_drawerbank, ah_drawerbranch,
    ah_invoiceapproved, ah_consolidatedinvoiceref, ah_fullypaiddate,
    ah_invoiceprinted, ah_iscancelled, ah_dateclearedincashbook,
    ah_notallocated, ah_outstandingamount, ah_postedtoeft, ah_posttogl,
    ah_receiptbatchno, ah_transactioncreatedbymatching,
    ah_invoiceterm, ah_invoicetermdays, ah_requisitiondate, ah_requisitionstatus,
    ah_numberofsupportingdocuments, ah_exportbatchnumber, ah_postedinternal,
    ah_post1, ah_post2, ah_post3, ah_post4,
    ah_ab, ah_oh, ah_oa_invoiceaddressoverride, ah_oc_invoicecontactoverride,
    ah_jh, ah_gb, ah_gc, ah_ge, ah_ag,
    ah_transactionbelongstogroup, ah_ah_invoicestatement,
    ah_systemcreatetimeutc, ah_systemcreateuser,
    ah_systemlastedittimeutc, ah_systemlastedituser,
    ah_agreedpaymentmethodoverride, ah_compliancedocumentdate,
    ah_gs_nkauditedby, ah_gs_nkcashier, ah_invoicepaymentreferencecode,
    ah_localtaxamountothertaxes, ah_ostaxamountothertaxes, ah_autoversion,
    ah_documentreceiveddate, ah_matchstatus, ah_matchstatusreasoncode,
    ah_originalinvoicedate, ah_originaltransactionnum,
    ah_placeofsupply, ah_placeofsupplytype, ah_xd_compliancebook,
    ah_localtotal, ah_jobnumber,
    ah_originalreferenceenddate, ah_originalreferencestartdate,
    ah_gb_taxbranch, ah_governmentallocatedid, ah_cah_cashadvancerequestheader,
    ah_isosoutstandingamountapplicable, ah_osoutstandingamount, ah_overrideexchangerate,
    ah_systemcreatebranch, ah_systemcreatedepartment
)
SELECT
    @ahpk, @ledger, 'INV', t.ah_compliancesubtype, @invno,
    t.ah_transactioncount, t.ah_transactionreference, @desc,
    @invdate, @invdate, @amt, @gst, @wht,
    @ostotal, @ccy, @rate,
    t.ah_ageperiod, t.ah_postperiod, NULL,
    t.ah_transactioncategory, t.ah_chequeorreference, t.ah_receipttype,
    t.ah_cashbasisgstindicator, t.ah_cashbasisgstrealisedtogl,
    t.ah_chequedrawer, t.ah_drawerbank, t.ah_drawerbranch,
    0, t.ah_consolidatedinvoiceref, NULL,
    0, 0, NULL,
    0, @amt, 0, t.ah_posttogl,
    t.ah_receiptbatchno, 0,
    t.ah_invoiceterm, t.ah_invoicetermdays, NULL, t.ah_requisitionstatus,
    0, 0, 0,
    0, 0, 0, 0,
    NULL, @oh, NULL, NULL,
    @jh, @gb, @gc, @ge, NULL,
    t.ah_transactionbelongstogroup, NULL,
    @now, @user,
    NULL, @user,
    t.ah_agreedpaymentmethodoverride, NULL,
    t.ah_gs_nkauditedby, t.ah_gs_nkcashier, t.ah_invoicepaymentreferencecode,
    0, 0, t.ah_autoversion,
    NULL, t.ah_matchstatus, t.ah_matchstatusreasoncode,
    NULL, t.ah_originaltransactionnum,
    t.ah_placeofsupply, t.ah_placeofsupplytype, t.ah_xd_compliancebook,
    @amt, @jobnum,
    NULL, NULL,
    t.ah_gb_taxbranch, NULL, NULL,
    0, @ostotal, t.ah_overrideexchangerate,
    t.ah_systemcreatebranch, t.ah_systemcreatedepartment
FROM AccTransactionHeader t
WHERE t.ah_pk = @templatePk
";

        // INSERT AccTransactionLines（草稿：al_postdate = NULL）
        private const string InsertDraftLineSql = @"
INSERT INTO AccTransactionLines (
    al_pk, al_linetype, al_sequence, al_desc, al_lineamount, al_at, al_gstvat, al_gstvatbasis,
    al_a9_vatclass, al_aw, al_withholdingtax, al_unitqty, al_unitprice, al_osunitprice, al_osamount,
    al_rx_nktransactioncurrency, al_exchangerate, al_inputgstvatrecoverable, al_postperiod, al_postdate,
    al_posttogl, al_reverseperiod, al_reversedate, al_reversetogl, al_preventinvoiceprintgrouping,
    al_exportbatchnumber, al_exportreversebatchnumber, al_isfinalcharge, al_revrecognitiontype, al_jh,
    al_ac, al_ge, al_gb, al_ag, al_oh, al_ag_percentof, al_percentageofperiod, al_ah, al_gc,
    al_systemcreatetimeutc, al_systemcreateuser, al_systemlastedittimeutc, al_systemlastedituser,
    al_govtchargecode, al_gstvatextra, al_taxdate, al_taxextraratedenominator, al_taxextraratenumerator,
    al_taxratedenominator, al_taxratenumerator, al_autoversion, al_jbb, al_placeofsupply,
    al_placeofsupplytype, al_supplytype, al_gb_taxbranch
)
SELECT
    @alpk, @altype, @seq, @desc, @localamt, @gstcode, @gst, t.al_gstvatbasis,
    @vat, @whtcode, 0, 1, @osamt, @osamt, @osamt,
    @ccy, @rate, 0, t.al_postperiod, NULL,
    t.al_posttogl, t.al_reverseperiod, NULL, t.al_reversetogl, 0,
    0, 0, 0, t.al_revrecognitiontype, @jh,
    t.al_ac, @ge, @gb, t.al_ag, @oh, t.al_ag_percentof, 0, @ah, @gc,
    @now, @user, NULL, @user,
    t.al_govtchargecode, 0, NULL, 0, 0,
    0, 0, t.al_autoversion, t.al_jbb, t.al_placeofsupply,
    t.al_placeofsupplytype, t.al_supplytype, NULL
FROM AccTransactionLines t
WHERE t.al_pk = @templatePk
";

        // ---- 写操作用到的内部行模型 ----
        private class JobHeaderCtx
        {
            public string jh_pk { get; set; }
            public string jh_gb { get; set; }
            public string jh_gc { get; set; }
            public string jh_ge { get; set; }
            public string jh_jobnum { get; set; }
        }

        private class ConsolRefRow
        {
            public string consign_ref { get; set; }
            public string transport_mode { get; set; }
        }

        private class ChargeDraftRow
        {
            public string jr_pk { get; set; }
            public string jr_jh { get; set; }
            public string jr_chargetype { get; set; }
            public string jr_desc { get; set; }
            public int jr_displaysequence { get; set; }
            public string party_oh { get; set; }
            public string currency { get; set; }
            public decimal? exchange_rate { get; set; }
            public decimal? os_amount { get; set; }
            public decimal? amount { get; set; }
            public string gst_rate { get; set; }
            public string wht_rate { get; set; }
            public string vat_class { get; set; }
            public string jh_pk { get; set; }
            public string anchor_pk { get; set; }
            public string jh_jobnum { get; set; }
            public string jh_gb { get; set; }
            public string jh_gc { get; set; }
            public string jh_ge { get; set; }
        }

        private class HeaderPostRow
        {
            public string ah_pk { get; set; }
            public string ah_ledger { get; set; }
            public DateTime? ah_postdate { get; set; }
        }

        private class HeaderVoidRow
        {
            public string ah_pk { get; set; }
            public string ah_ledger { get; set; }
            public DateTime? ah_postdate { get; set; }
            public decimal ah_outstandingamount { get; set; }
            public decimal ah_invoiceamount { get; set; }
            public string ah_jh { get; set; }
        }

        private class AnchorRow
        {
            public string anchor_pk { get; set; }
            public string parent_table_code { get; set; }
        }

        private class HeaderEditRow
        {
            public string ah_pk { get; set; }
            public string ah_ledger { get; set; }
            public DateTime? ah_postdate { get; set; }
            public string ah_jh { get; set; }
            public string ah_gb { get; set; }
            public string ah_gc { get; set; }
            public string ah_ge { get; set; }
            public string ah_jobnumber { get; set; }
        }
    }
}
