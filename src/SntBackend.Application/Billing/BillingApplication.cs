using Dapper;
using Facade;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Billing.Dto.MatchTransaction;
using SntBackend.Application.Pdf;
using SntBackend.Application.Po.Dto;
using SntBackend.DomainService.Folders;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Billing
{
    public class BillingApplication : SntBackendApplicationBase, IBillingApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        private readonly InvoicePdfDataProvider _invoicePdfDataProvider;
        private readonly PlaywrightPdfGenerator _playwrightPdfGenerator;
        private readonly IAppFolders _appFolders;
        private readonly BillingCore _billingCore;

        public BillingApplication(
            IAppSqlServerRepository appSqlServerRepository,
            InvoicePdfDataProvider invoicePdfDataProvider,
            PlaywrightPdfGenerator playwrightPdfGenerator,
            IAppFolders appFolders,
            BillingCore billingCore)
        {
            _appSqlServerRepository = appSqlServerRepository;
            _invoicePdfDataProvider = invoicePdfDataProvider;
            _playwrightPdfGenerator = playwrightPdfGenerator;
            _appFolders = appFolders;
            _billingCore = billingCore;
        }

        /// <summary>
        /// AccTransactionMatchLink 的 ap_systemcreateuser / ap_systemlastedituser 都是 varchar(3)
        /// （库内实际取值形如 'ZOZ'、'KT'），超长会直接插入失败。
        /// </summary>
        private const string MatchLinkUser = "sys";

        /// <summary>
        /// 一次核销写两条 AccTransactionMatchLink：被核销的发票 + 新建的 REC/PAY，
        /// 同一 ap_matchgroupnum、金额一正一负（与库内既有核销组的形态一致）。
        /// </summary>
        private const string InsertMatchLinkPairSql = @"
INSERT INTO AccTransactionMatchLink (
    ap_pk, ap_amount, ap_gstrealised, ap_matchgroupnum, ap_matchperiod, ap_matchdate,
    ap_ah, ap_reason, ap_systemcreateuser, ap_systemlastedittimeutc, ap_systemlastedituser,
    ap_systemcreatetimeutc, ap_osamount
)
SELECT NEWID(), @invAmount,     0, @matchGroup, 0, @matchDate, @invPk,     '', @user, @nowUtc, @user, @nowUtc, 0
UNION ALL
SELECT NEWID(), @counterAmount, 0, @matchGroup, 0, @matchDate, @counterPk, '', @user, @nowUtc, @user, @nowUtc, 0
";

        private static string TblBuildWhere(List<BillingTblFilterItem> filters, DynamicParameters dp)
        {
            var parts = new List<string>();

            static string MapOp(string op) => op switch
            {
                "=" => "=",
                ">" => ">",
                "<" => "<",
                ">=" => ">=",
                "<=" => "<=",
                "Contain" => "LIKE",
                "Not Contain" => "NOT LIKE",
                _ => "="
            };

            foreach (var item in filters)
            {
                if (string.IsNullOrWhiteSpace(item.key)) continue;

                if (item.op == "between")
                {
                    if (!string.IsNullOrWhiteSpace(item.start))
                    {
                        var paramNameStart = $"@p{dp.ParameterNames.Count()}";
                        parts.Add($" AND t.{item.key} >= {paramNameStart} ");
                        dp.Add(paramNameStart, item.start);
                    }
                    if (!string.IsNullOrWhiteSpace(item.end))
                    {
                        var paramNameEnd = $"@p{dp.ParameterNames.Count()}";
                        parts.Add($" AND t.{item.key} <= {paramNameEnd}");
                        dp.Add(paramNameEnd, item.end);
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(item.val)) continue;
                    var val = item.val.Trim();
                    var paramName = $"@p{dp.ParameterNames.Count()}";
                    var isContain = item.op == "Contain" || item.op == "Not Contain";
                    parts.Add($" AND t.{item.key} {MapOp(item.op)} {paramName}");
                    dp.Add(paramName, isContain ? $"%{val}%" : val);
                }
            }

            return string.Join(" ", parts);
        }

        public async Task<BillingTblOutput> ApTbl(BillingTblInput input)
        {
            return await GetPagedList("AP", input);
        }

        public async Task<BillingTblOutput> ArTbl(BillingTblInput input)
        {
            return await GetPagedList("AR", input);
        }

        private async Task<BillingTblOutput> GetPagedList(string ledger, BillingTblInput input)
        {
            var output = new BillingTblOutput();
            var dp = new DynamicParameters();
            dp.Add("ledger", ledger);
            var whereIf = TblBuildWhere(input.filters, dp);

            var totalSql = @$"
SELECT COUNT(*)
FROM AccTransactionHeader t
WHERE 1 = 1
    AND t.ah_ledger = @ledger
    AND t.ah_iscancelled = 0
    {whereIf}
";
            var pageSql = @$"
SELECT t.*, o.oh_fullname
FROM AccTransactionHeader t
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE 1 = 1
    AND t.ah_ledger = @ledger
    AND t.ah_iscancelled = 0
    {whereIf}
ORDER BY t.ah_invoicedate desc, t.ah_pk desc
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";
            dp.Add("skipCount", input.SkipCount);
            dp.Add("takeCount", input.MaxResultCount);

            using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql}
", dp))
            {
                var total = await multi.ReadFirstAsync<int>();
                var list = (await multi.ReadAsync<AccTransactionHeaderDtoOutput>()).ToList();

                output.TotalCount = total;
                output.Items = list;
            }

            return output;
        }

        public async Task<AccTransactionHeaderDtoOutput> Detail(string id)
        {
            var dp = new DynamicParameters();
            dp.Add("id", id);

            var sql = @"
SELECT t.*, o.oh_fullname
FROM AccTransactionHeader t
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_pk = @id
";
            return await _appSqlServerRepository.QueryFirstOrDefaultAsync<AccTransactionHeaderDtoOutput>(sql, dp);
        }

        public async Task<WriteOffTblOutput> WriteOffTbl(WriteOffTblInput input)
        {
            var output = new WriteOffTblOutput();
            var dp = new DynamicParameters();
            var whereIf = TblBuildWhere(input.filters, dp);

            var totalSql = @$"
SELECT COUNT(*)
FROM AccTransactionMatchLink m
INNER JOIN AccTransactionHeader t ON t.ah_pk = m.ap_ah
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_fullypaiddate IS NOT NULL
    AND t.ah_iscancelled = 0
    AND t.ah_transactiontype IN ('REC', 'PAY', 'INV')
    {whereIf}
";
            var pageSql = @$"
SELECT m.ap_pk, m.ap_amount, m.ap_matchdate, m.ap_systemcreatetimeutc,
       m.ap_reason, m.ap_ah,
       t.ah_transactionnum, t.ah_rx_nktransactioncurrency,
       t.ah_matchstatus, t.ah_transactiontype,
       o.oh_fullname AS CompanyName
FROM AccTransactionMatchLink m
INNER JOIN AccTransactionHeader t ON t.ah_pk = m.ap_ah
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_fullypaiddate IS NOT NULL
    AND t.ah_iscancelled = 0
    AND t.ah_transactiontype IN ('REC', 'PAY', 'INV')
    {whereIf}
ORDER BY m.ap_matchdate DESC, m.ap_pk DESC
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";
            dp.Add("skipCount", input.SkipCount);
            dp.Add("takeCount", input.MaxResultCount);

            using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql}
", dp))
            {
                var total = await multi.ReadFirstAsync<int>();
                var list = (await multi.ReadAsync<WriteOffTblItem>()).ToList();

                output.TotalCount = total;
                output.Items = list;
            }

            return output;
        }

        public async Task<WriteOffDetailOutput> WriteOffDetail(WriteOffDetailInput input)
        {
            var dp = new DynamicParameters();
            dp.Add("id", input.apPk);

            // 1. 通过 ap_pk 查询 MatchLink 本身
            var matchLinkSql = @"
SELECT m.*
FROM AccTransactionMatchLink m
WHERE m.ap_pk = @id
";
            var matchLink = await _appSqlServerRepository.QueryFirstOrDefaultAsync<AccTransactionMatchLinkDtoOutput>(matchLinkSql, dp);
            if (matchLink == null) return null;

            // 2. 通过 MatchLink 的 ap_ah 找到关联的 Header
            var headerSql = @"
SELECT t.ah_pk, t.ah_transactionnum, o.oh_fullname AS CompanyName,
       t.ah_invoiceamount, t.ah_rx_nktransactioncurrency, t.ah_fullypaiddate,
       t.ah_matchstatus, t.ah_systemcreatetimeutc, t.ah_desc,
       t.ah_transactiontype, t.ah_ledger, t.ah_outstandingamount, t.ah_ostotal
FROM AccTransactionHeader t
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_pk = @ahPk
";
            var hdp = new DynamicParameters();
            hdp.Add("ahPk", matchLink.ap_ah);
            var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<WriteOffDetailHeader>(headerSql, hdp);

            // 3. 通过 Header 的 ah_pk 查询所有 AccTransactionLines
            var linesSql = @"
SELECT l.*
FROM AccTransactionLines l
WHERE l.al_ah = @ahPk
ORDER BY l.al_sequence
";
            var ldp = new DynamicParameters();
            ldp.Add("ahPk", matchLink.ap_ah);
            var transactionLines = (await _appSqlServerRepository.QueryAsync<AccTransactionLinesDtoOutput>(linesSql, ldp)).ToList();

            return new WriteOffDetailOutput
            {
                MatchLink = matchLink,
                Header = header,
                TransactionLines = transactionLines
            };
        }

        public async Task<OutstandingInvoiceOutput> QueryOutstandingInvoices(OutstandingInvoiceInput input)
        {
            var output = new OutstandingInvoiceOutput();
            var dp = new DynamicParameters();
            var whereIf = "";

            if (!string.IsNullOrWhiteSpace(input.BillingParty))
            {
                whereIf += " AND t.ah_oh = @billingParty ";
                dp.Add("billingParty", input.BillingParty);
            }

            if (!string.IsNullOrWhiteSpace(input.LedgerScope) && input.LedgerScope != "BOTH")
            {
                whereIf += " AND t.ah_ledger = @ledgerScope ";
                dp.Add("ledgerScope", input.LedgerScope);
            }

            if (!string.IsNullOrWhiteSpace(input.Query))
            {
                whereIf += " AND (t.ah_jobnumber LIKE @query OR t.ah_transactionnum LIKE @query OR t.ah_desc LIKE @query) ";
                dp.Add("query", $"%{input.Query}%");
            }

            if (!string.IsNullOrWhiteSpace(input.StatementNo))
            {
                whereIf += " AND (t.ah_ah_invoicestatement LIKE @statementNo OR t.ah_transactionnum LIKE @statementNo) ";
                dp.Add("statementNo", $"%{input.StatementNo}%");
            }

            if (!string.IsNullOrWhiteSpace(input.Currency))
            {
                whereIf += " AND t.ah_rx_nktransactioncurrency LIKE @currency ";
                dp.Add("currency", $"%{input.Currency}%");
            }

            if (!string.IsNullOrWhiteSpace(input.ChargeDesc))
            {
                whereIf += @" AND EXISTS (
                    SELECT 1 FROM AccTransactionLines l
                    WHERE l.al_ah = t.ah_pk AND l.al_desc LIKE @chargeDesc
                ) ";
                dp.Add("chargeDesc", $"%{input.ChargeDesc}%");
            }

            var skipCount = input.PageIndex * input.PageSize;
            dp.Add("skipCount", skipCount);
            dp.Add("takeCount", input.PageSize);

            var totalSql = @$"
SELECT COUNT(*)
FROM AccTransactionHeader t
WHERE t.ah_iscancelled = 0
    AND t.ah_outstandingamount != 0
    AND t.ah_transactiontype IN ('INV', 'BILL')
    {whereIf}
";
            var pageSql = @$"
SELECT
    t.ah_pk AS Id,
    t.ah_pk AS TthPk,
    t.ah_ledger AS Ledger,
    t.ah_jobnumber AS JobNo,
    t.ah_transactionnum AS TaxInvoiceNo,
    t.ah_transactionnum AS InvoiceNumber,
    t.ah_invoicedate AS BillingDate,
    t.ah_outstandingamount AS Outstanding,
    t.ah_ostotal AS SettlementAmountOriginal,
    CASE WHEN t.ah_exchangerate = 0 THEN 1 ELSE t.ah_exchangerate END AS ExRate,
    t.ah_outstandingamount AS SettlementAmountHome,
    t.ah_rx_nktransactioncurrency AS Currency
FROM AccTransactionHeader t
WHERE t.ah_iscancelled = 0
    AND t.ah_outstandingamount != 0
    AND t.ah_transactiontype IN ('INV', 'BILL')
    {whereIf}
ORDER BY t.ah_invoicedate DESC, t.ah_pk DESC
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";

            using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql}
", dp))
            {
                output.TotalCount = await multi.ReadFirstAsync<int>();
                var items = (await multi.ReadAsync<OutstandingInvoiceItem>()).ToList();

                // 拼接费用描述
                if (items.Any() && string.IsNullOrWhiteSpace(input.ChargeDesc))
                {
                    var pks = items.Select(x => x.TthPk).ToList();
                    var chargeDescSql = @"
SELECT al_ah, al_desc
FROM AccTransactionLines
WHERE al_ah IN @pks
ORDER BY al_ah, al_sequence
";
                    var chargeLines = (await _appSqlServerRepository.QueryAsync<(string al_ah, string al_desc)>(
                        chargeDescSql, new { pks })).ToList();

                    var chargeMap = chargeLines
                        .GroupBy(x => x.al_ah)
                        .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(x => x.al_desc).Where(d => !string.IsNullOrWhiteSpace(d))));

                    foreach (var item in items)
                    {
                        if (chargeMap.TryGetValue(item.TthPk, out var desc))
                            item.ChargeDesc = desc;
                    }
                }

                output.Items = items;
            }

            return output;
        }

        public async Task<SaveMatchWriteOffOutput> SaveMatchWriteOff(SaveMatchWriteOffInput input)
        {
            try
            {
            if (input.Lines == null || !input.Lines.Any())
                throw new Exception("Lines cannot be empty.");

            if (!input.SettleDate.HasValue)
                throw new Exception("SettleDate is required.");

            var matchNumber = string.IsNullOrWhiteSpace(input.MatchNumber)
                ? $"MCH-{DateTime.Now:yyyyMMddHHmmssfff}"
                : input.MatchNumber;

            // 核销组号（写入 AccTransactionMatchLink.ap_matchgroupnum，varchar(20)）。
            // matchNumber 本身可能超过 20 字符，且库内 CargoWise 自己的编号是 M+8 位数字（M00075287），
            // 这里用 M+15 位时间戳，长度合规且永远不会和它的序列撞号。
            var matchGroupNum = "M" + DateTime.Now.ToString("yyMMddHHmmssfff");

            var mode = (input.Mode ?? "").ToLower();
            var isReceipt = mode == "receipt";
            var transactionType = isReceipt ? "REC" : "PAY";
            var expectedLedger = isReceipt ? "AR" : "AP";

            var remainingAmount = input.SettleAmount;
            var newHeaderPks = new List<string>();
            var affectedCount = 0;
            var totalWriteOffOriginal = 0m;
            var totalWriteOffHome = 0m;

            foreach (var line in input.Lines)
            {
                if (remainingAmount <= 0) break;

                // 查询发票信息
                var invDp = new DynamicParameters();
                invDp.Add("pk", line.TthPk);
                var invSql = @"
SELECT t.ah_ledger, t.ah_transactiontype, t.ah_transactionnum,
       t.ah_outstandingamount, t.ah_ostotal, t.ah_exchangerate,
       t.ah_iscancelled
FROM AccTransactionHeader t
WHERE t.ah_pk = @pk
";
                var invoice = await _appSqlServerRepository.QueryFirstOrDefaultAsync<dynamic>(invSql, invDp);
                if (invoice == null) continue;

                // 校验
                string invType = invoice.ah_transactiontype;
                if (invType != "INV" && invType != "BILL") continue;
                if ((bool)invoice.ah_iscancelled) continue;

                string invLedger = invoice.ah_ledger;
                if (invLedger != expectedLedger) continue;

                decimal outstanding = invoice.ah_outstandingamount;
                decimal osTotal = invoice.ah_ostotal;
                if (outstanding == 0 && osTotal == 0) continue;

                decimal exRate = invoice.ah_exchangerate;
                if (exRate == 0) exRate = 1;

                // 计算核销金额（本位币）
                var writeOffHome = Math.Min(Math.Abs(outstanding), remainingAmount);
                // AP 的 outstanding 是负数
                if (!isReceipt) writeOffHome = Math.Min(Math.Abs(outstanding), remainingAmount);

                var writeOffOriginal = exRate != 0 ? writeOffHome / exRate : writeOffHome;

                remainingAmount -= writeOffHome;

                // 新建 REC/PAY Header — 从原 INV/BILL 整行复制再覆盖核销专属字段
                // 这样所有 NOT NULL 列都自动满足约束（如 ah_systemcreatedepartment 等）
                var newPk = Guid.NewGuid().ToString();
                var description = string.IsNullOrWhiteSpace(input.Description)
                    ? $"Match Write Off - {((object)invoice.ah_transactionnum)?.ToString()}"
                    : input.Description;
                var amount = isReceipt ? writeOffHome : -writeOffHome;

                var insertHeaderSql = @"
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
    @newPk, t.ah_ledger, @transType, t.ah_compliancesubtype, @matchNumber,
    t.ah_transactioncount, t.ah_transactionreference, @desc,
    @settleDate, @settleDate, @amount, 0, 0,
    0, t.ah_rx_nktransactioncurrency, t.ah_exchangerate,
    t.ah_ageperiod, t.ah_postperiod, @settleDate,
    t.ah_transactioncategory, @chequeNo, t.ah_receipttype,
    t.ah_cashbasisgstindicator, t.ah_cashbasisgstrealisedtogl,
    t.ah_chequedrawer, t.ah_drawerbank, t.ah_drawerbranch,
    t.ah_invoiceapproved, t.ah_consolidatedinvoiceref, @settleDate,
    t.ah_invoiceprinted, 0, t.ah_dateclearedincashbook,
    t.ah_notallocated, 0, t.ah_postedtoeft, t.ah_posttogl,
    t.ah_receiptbatchno, 1,
    t.ah_invoiceterm, t.ah_invoicetermdays, t.ah_requisitiondate, t.ah_requisitionstatus,
    t.ah_numberofsupportingdocuments, t.ah_exportbatchnumber, t.ah_postedinternal,
    t.ah_post1, t.ah_post2, t.ah_post3, t.ah_post4,
    @bankPk, t.ah_oh, t.ah_oa_invoiceaddressoverride, t.ah_oc_invoicecontactoverride,
    t.ah_jh, t.ah_gb, t.ah_gc, t.ah_ge, t.ah_ag,
    t.ah_transactionbelongstogroup, t.ah_ah_invoicestatement,
    @now, t.ah_systemcreateuser,
    NULL, t.ah_systemcreateuser,
    t.ah_agreedpaymentmethodoverride, t.ah_compliancedocumentdate,
    t.ah_gs_nkauditedby, t.ah_gs_nkcashier, t.ah_invoicepaymentreferencecode,
    0, 0, t.ah_autoversion,
    t.ah_documentreceiveddate, t.ah_matchstatus, t.ah_matchstatusreasoncode,
    t.ah_originalinvoicedate, t.ah_transactionnum,
    t.ah_placeofsupply, t.ah_placeofsupplytype, t.ah_xd_compliancebook,
    @amount, t.ah_jobnumber,
    t.ah_originalreferenceenddate, t.ah_originalreferencestartdate,
    t.ah_gb_taxbranch, t.ah_governmentallocatedid, t.ah_cah_cashadvancerequestheader,
    t.ah_isosoutstandingamountapplicable, 0, t.ah_overrideexchangerate,
    t.ah_systemcreatebranch, t.ah_systemcreatedepartment
FROM AccTransactionHeader t
WHERE t.ah_pk = @origPk
";
                var headerDp = new DynamicParameters();
                headerDp.Add("newPk", newPk);
                headerDp.Add("transType", transactionType);
                headerDp.Add("matchNumber", matchNumber);
                headerDp.Add("desc", description);
                headerDp.Add("settleDate", input.SettleDate.Value);
                headerDp.Add("amount", amount);
                headerDp.Add("chequeNo", input.ChequeNo ?? string.Empty);
                headerDp.Add("bankPk", string.IsNullOrWhiteSpace(input.BankPK) ? null : input.BankPK);
                headerDp.Add("now", DateTime.UtcNow);
                headerDp.Add("origPk", line.TthPk);

                await _appSqlServerRepository.ExecuteAsync(insertHeaderSql, headerDp);
                newHeaderPks.Add(newPk);

                // 更新原发票 outstanding
                var newOutstanding = outstanding - (isReceipt ? writeOffHome : -writeOffHome);
                var newOsTotal = osTotal - (isReceipt ? writeOffOriginal : -writeOffOriginal);
                var isFullyPaid = Math.Abs(newOutstanding) < 0.01m;

                var updateInvSql = @"
UPDATE AccTransactionHeader
SET ah_outstandingamount = @newOutstanding,
    ah_ostotal = @newOsTotal,
    ah_fullypaiddate = CASE WHEN @isFullyPaid = 1 THEN @settleDate ELSE ah_fullypaiddate END,
    ah_systemlastedittimeutc = @now
WHERE ah_pk = @pk
";
                var updDp = new DynamicParameters();
                updDp.Add("newOutstanding", isFullyPaid ? 0m : newOutstanding);
                updDp.Add("newOsTotal", isFullyPaid ? 0m : newOsTotal);
                updDp.Add("isFullyPaid", isFullyPaid ? 1 : 0);
                updDp.Add("settleDate", input.SettleDate.Value);
                updDp.Add("now", DateTime.UtcNow);
                updDp.Add("pk", line.TthPk);

                await _appSqlServerRepository.ExecuteAsync(updateInvSql, updDp);

                // 核销链接：原发票 + 新建的 REC/PAY 各一条，同一 ap_matchgroupnum，金额一正一负。
                // 少了这两行，销账列表(/reconciliation/writeoff/tbl、writeoff/detail)查不到本次核销
                // —— 那两个接口是从 AccTransactionMatchLink 起查的。
                // ap_matchdate 存本地时间、ap_system*utc 存 UTC（与库内既有数据一致）。
                var invoiceLegAmount = isReceipt ? writeOffHome : -writeOffHome;
                var linkDp = new DynamicParameters();
                linkDp.Add("matchGroup", matchGroupNum);
                linkDp.Add("matchDate", input.SettleDate.Value);
                linkDp.Add("invPk", line.TthPk);
                linkDp.Add("counterPk", newPk);
                linkDp.Add("invAmount", invoiceLegAmount);
                linkDp.Add("counterAmount", -invoiceLegAmount);
                linkDp.Add("nowUtc", DateTime.UtcNow);
                linkDp.Add("user", MatchLinkUser);
                await _appSqlServerRepository.ExecuteAsync(InsertMatchLinkPairSql, linkDp);

                affectedCount++;
                totalWriteOffOriginal += writeOffOriginal;
                totalWriteOffHome += writeOffHome;
            }

            return new SaveMatchWriteOffOutput
            {
                MatchNumber = matchNumber,
                TransactionHeaderPks = newHeaderPks,
                AffectedInvoiceCount = affectedCount,
                TotalWriteOffAmountOriginal = totalWriteOffOriginal,
                TotalWriteOffAmountHome = totalWriteOffHome
            };
            }
            catch (Exception ex)
            {
                Console.WriteLine("=========== SaveMatchWriteOff ERROR ===========");
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
                Console.WriteLine("===============================================");
                throw;
            }
        }

        public async Task<MatchTransactionPageOutput> QueryMatchTransactionPage(MatchTransactionPageInput input)
        {
            var output = new MatchTransactionPageOutput();
            var dp = new DynamicParameters();
            var whereIf = "";

            if (!string.IsNullOrWhiteSpace(input.Shipper))
            {
                whereIf += " AND t.ah_oh = @shipper ";
                dp.Add("shipper", input.Shipper);
            }

            if (!string.IsNullOrWhiteSpace(input.JobNumber))
            {
                whereIf += " AND t.ah_jobnumber LIKE @jobNumber ";
                dp.Add("jobNumber", $"%{input.JobNumber}%");
            }

            if (!string.IsNullOrWhiteSpace(input.MatchNumber))
            {
                whereIf += " AND t.ah_transactionnum LIKE @matchNumber ";
                dp.Add("matchNumber", $"%{input.MatchNumber}%");
            }

            dp.Add("skipCount", input.SkipCount);
            dp.Add("takeCount", input.MaxResultCount);

            var totalSql = @$"
SELECT COUNT(*)
FROM AccTransactionHeader t
WHERE t.ah_iscancelled = 0
    AND t.ah_transactiontype IN ('REC', 'PAY', 'INV')
    {whereIf}
";
            var pageSql = @$"
SELECT
    t.ah_pk AS Pk,
    t.ah_ledger AS Ledger,
    t.ah_transactionnum AS MatchNumber,
    t.ah_oh AS BillingParty,
    o.oh_fullname AS BillingPartyName,
    t.ah_rx_nktransactioncurrency AS Currency,
    t.ah_invoiceamount AS SettledAmount,
    CONVERT(varchar(10), t.ah_invoicedate, 23) AS PaymentDate,
    t.ah_desc AS Description
FROM AccTransactionHeader t
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_iscancelled = 0
    AND t.ah_transactiontype IN ('REC', 'PAY', 'INV')
    {whereIf}
ORDER BY t.ah_invoicedate DESC, t.ah_pk DESC
OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
";

            using (var multi = await _appSqlServerRepository.QueryMultipleAsync($@"
{totalSql};
{pageSql}
", dp))
            {
                output.TotalCount = await multi.ReadFirstAsync<int>();
                output.Items = (await multi.ReadAsync<MatchTransactionPageItem>()).ToList();
            }

            return output;
        }

        public async Task<List<AccTransactionLinesDtoOutput>> QueryMatchTransactionLines(MatchTransactionLinesInput input)
        {
            var dp = new DynamicParameters();
            dp.Add("tthPk", input.TthPk);

            var sql = @"
SELECT l.*
FROM AccTransactionLines l
WHERE l.al_ah = @tthPk
ORDER BY l.al_sequence
";
            return (await _appSqlServerRepository.QueryAsync<AccTransactionLinesDtoOutput>(sql, dp)).ToList();
        }

        public async Task<List<AccBankAccountDtoOutput>> QueryWriteOffBank(WriteOffBankInput input)
        {
            var dp = new DynamicParameters();
            var whereIf = "";

            if (!string.IsNullOrWhiteSpace(input?.SettleCompanyName))
            {
                whereIf += " AND b.ab_bankname LIKE @bankName ";
                dp.Add("bankName", $"{input.SettleCompanyName.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(input?.SettleCompanyCode))
            {
                whereIf += " AND b.ab_code LIKE @code ";
                dp.Add("code", $"{input.SettleCompanyCode.Trim()}%");
            }

            var sql = @$"
SELECT b.*
FROM AccBankAccount b
WHERE ISNULL(b.ab_isactive, 0) = 1
    AND ISNULL(b.ab_accountnum, '') <> ''
    {whereIf}
ORDER BY b.ab_accountnum
";
            return (await _appSqlServerRepository.QueryAsync<AccBankAccountDtoOutput>(sql, dp)).ToList();
        }

        public async Task<MatchTransactionDetailOutput> MatchTransactionDetail(string pk)
        {
            try
            {
                var dp = new DynamicParameters();
                dp.Add("pk", pk);

                var sql = @"SELECT * FROM AccTransactionHeader WHERE ah_pk = @pk";
                var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<AccTransactionHeaderDtoOutput>(sql, dp);

                if (header == null) return null;

                MatchTransactionDetailOutput detail = new MatchTransactionDetailOutput { Header = header };

                if (!string.IsNullOrWhiteSpace(header.ah_ab))
                {
                    var bankDp = new DynamicParameters();
                    bankDp.Add("ab_pk", header.ah_ab);
                    var bankSql = @"SELECT * FROM AccBankAccount WHERE ab_pk = @ab_pk";
                    var bank = await _appSqlServerRepository.QueryFirstOrDefaultAsync<AccBankAccountDtoOutput>(bankSql, bankDp);
                    if (bank != null)
                    {
                        detail.Bank = bank;
                    }
                }

                var h = detail.Header;
                detail.Lines = new List<OutstandingInvoiceItem>
                {
                    new OutstandingInvoiceItem
                    {
                        Id = h.ah_pk,
                        TthPk = h.ah_pk,
                        Ledger = h.ah_ledger,
                        JobNo = h.ah_jobnumber,
                        TaxInvoiceNo = h.ah_transactionnum,
                        InvoiceNumber = h.ah_transactionnum,
                        BillingDate = h.ah_invoicedate,
                        ChargeDesc = h.ah_desc,
                        Outstanding = h.ah_outstandingamount,
                        SettlementAmountOriginal = h.ah_ostotal,
                        ExRate = h.ah_exchangerate,
                        SettlementAmountHome = h.ah_invoiceamount,
                        Currency = h.ah_rx_nktransactioncurrency
                    }
                };

                return detail;
            }
            catch (Exception ex)
            {
                Console.WriteLine("=========== MatchTransactionDetail ERROR ===========");
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
                Console.WriteLine("====================================================");
                throw;
            }
        }

        // ---- 以下锚点相关的读操作统一委托 BillingCore（shipment 作用域）----
        // 合单(JobConsol) 作用域的同名能力见 Consolidation/ConsolidationBillingApplication.cs

        public Task<BillingChargeLineOutput> QueryChargeLine(BillingChargeLineInput input) =>
            _billingCore.QueryChargeLineAsync(BillingScope.Shipment, input?.shpPk, input?.chargeType,
                input?.SkipCount ?? 0, input?.MaxResultCount ?? 20, input?.Sorting);

        public Task<BillingDraftPageOutput> QueryDraftPage(BillingDraftPageInput input) =>
            _billingCore.QueryDraftPageAsync(BillingScope.Shipment, input?.shpPk, input?.chargeType,
                input?.SkipCount ?? 0, input?.MaxResultCount ?? 20, input?.Sorting);

        public Task<BillingSummaryDto> GetBillingSummary(string shpPk) =>
            _billingCore.GetBillingSummaryAsync(BillingScope.Shipment, shpPk);

        public async Task<QueryChargesByInvoiceOutput> QueryChargesByInvoiceNo(string invoiceNo)
        {
            if (string.IsNullOrWhiteSpace(invoiceNo))
                throw new System.Exception("invoiceNo cannot be empty.");

            var dp = new DynamicParameters();
            dp.Add("invoiceNo", invoiceNo);

            // 作废的发票不返回；但作废产生的 CRD 冲销单自身也是 ah_iscancelled=1（对齐库内惯例），
            // 前端点已作废发票要能看到这张冲销单，所以对 CRD 放行。同号时优先取仍然有效的那张。
            var headSql = @"
SELECT TOP 1 ah.*
FROM AccTransactionHeader ah
WHERE (ah.ah_transactionnum = @invoiceNo OR ah.ah_consolidatedinvoiceref = @invoiceNo)
    AND (ah.ah_iscancelled = 0 OR ah.ah_transactiontype = 'CRD')
ORDER BY ah.ah_iscancelled, ah.ah_invoicedate DESC, ah.ah_pk DESC
";
            var head = await _appSqlServerRepository.QueryFirstOrDefaultAsync<AccTransactionHeaderDtoOutput>(headSql, dp);
            if (head == null)
            {
                return new QueryChargesByInvoiceOutput();
            }

            var partyDp = new DynamicParameters();
            partyDp.Add("oh", head.ah_oh);
            var partySql = @"
SELECT TOP 1 oh.oh_fullname
FROM OrgHeader oh
WHERE oh.oh_pk = @oh
";
            var billingParty = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(partySql, partyDp);

            var linesDp = new DynamicParameters();
            linesDp.Add("ahPk", head.ah_pk);
            var linesSql = @"
SELECT al.*
FROM AccTransactionLines al
WHERE al.al_ah = @ahPk
ORDER BY al.al_sequence
";
            var lines = (await _appSqlServerRepository.QueryAsync<AccTransactionLinesDtoOutput>(linesSql, linesDp)).ToList();

            // 反查关联的预录费用 (JobCharge)，按发票的 ledger 决定取 AR/AP 侧
            var charges = new List<BillingChargeLineItem>();
            if (lines.Count > 0)
            {
                var isAr = string.Equals(head.ah_ledger, "AR", System.StringComparison.OrdinalIgnoreCase);
                var amountCol = isAr ? "jr.jr_localsellamt" : "jr.jr_localcostamt";
                var osAmountCol = isAr ? "jr.jr_ossellamt" : "jr.jr_oscostamt";
                var currencyCol = isAr ? "jr.jr_rx_nksellcurrency" : "jr.jr_rx_nkcostcurrency";
                var partyCol = isAr ? "jr.jr_oh_sellaccount" : "jr.jr_oh_costaccount";
                var rateCol = isAr ? "jr.jr_ossellexrate" : "jr.jr_oscostexrate";
                var gstCol = isAr ? "jr.jr_at_sellgstrate" : "jr.jr_at_costgstrate";
                var whtCol = isAr ? "jr.jr_aw_sellwhtrate" : "jr.jr_aw_costwhtrate";
                var vatCol = isAr ? "jr.jr_a9_sellvatclass" : "jr.jr_a9_costvatclass";
                var lineCol = isAr ? "jr.jr_al_arline" : "jr.jr_al_apline";

                var linePks = lines.Select(x => x.al_pk).ToList();

                var chargesSql = $@"
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
    {currencyCol}  AS currency,
    {partyCol}     AS party_oh,
    {rateCol}      AS exchange_rate,
    {gstCol}       AS gst_rate,
    {whtCol}       AS wht_rate,
    {vatCol}       AS vat_class,
    {lineCol}      AS line_pk,
    @ahPk          AS invoice_pk,
    @invoiceNo     AS invoice_no,
    NULL           AS invoice_date,
    'N'            AS Draft
FROM JobCharge jr
LEFT JOIN AccChargeCode cc ON cc.ac_pk = jr.jr_ac
WHERE {lineCol} IN @linePks
ORDER BY jr.jr_displaysequence, jr.jr_pk
";
                charges = (await _appSqlServerRepository.QueryAsync<BillingChargeLineItem>(chargesSql, new { linePks, ahPk = head.ah_pk, invoiceNo = invoiceNo })).ToList();
            }

            return new QueryChargesByInvoiceOutput
            {
                Head = head,
                Lines = lines,
                Charges = charges,
                BillingParty = billingParty
            };
        }

        public async Task<QueryOrgAddressOutput> QueryOrgAddress(QueryOrgAddressInput input)
        {
            var output = new QueryOrgAddressOutput();
            var dp = new DynamicParameters();
            var whereIf = "";

            if (!string.IsNullOrWhiteSpace(input.Query))
            {
                whereIf += " AND (UPPER(o.OH_FullName) LIKE UPPER(@queryLike) OR UPPER(o.OH_Code) LIKE UPPER(@queryLike)) ";
                dp.Add("queryLike", $"{input.Query.Trim()}%");
            }

            var sql = $@"
SELECT DISTINCT h.AH_OH, o.OH_FullName, o.OH_Code
FROM [dbo].[AccTransactionHeader] AS h
INNER JOIN [dbo].[OrgHeader] AS o ON o.OH_PK = h.AH_OH
WHERE h.AH_FullyPaidDate IS NULL
    AND h.AH_TransactionType = 'INV'
    {whereIf}
";
            output.List = (await _appSqlServerRepository.QueryAsync<QueryOrgAddressDto>(sql, dp)).ToList();
            return output;
        }

        public async Task<List<CurrencyOptionOutput>> CurrencyOptions(string query)
        {
            var dp = new DynamicParameters();
            var whereIf = "";

            if (!string.IsNullOrWhiteSpace(query))
            {
                whereIf += " AND (c.rx_code LIKE @code OR c.rx_desc LIKE @desc) ";
                var keyword = $"%{query.Trim()}%";
                dp.Add("code", keyword);
                dp.Add("desc", keyword);
            }

            var sql = $@"
SELECT c.rx_pk AS pk, c.rx_code AS code, c.rx_desc AS [desc]
FROM RefCurrency c
WHERE c.rx_isactive = 1
    {whereIf}
ORDER BY c.rx_code
";
            return (await _appSqlServerRepository.QueryAsync<CurrencyOptionOutput>(sql, dp)).ToList();
        }

        public async Task<List<ChargeCodeOptionOutput>> ChargeCodeOptions(string query)
        {
            var dp = new DynamicParameters();
            var whereIf = "";

            if (!string.IsNullOrWhiteSpace(query))
            {
                whereIf += " AND (c.ac_code LIKE @kw OR c.ac_desc LIKE @kw) ";
                dp.Add("kw", $"%{query.Trim()}%");
            }

            var sql = $@"
SELECT TOP 100
    c.ac_pk         AS pk,
    c.ac_code       AS code,
    c.ac_desc       AS [desc],
    c.ac_chargetype AS charge_type
FROM AccChargeCode c
WHERE c.ac_isactive = 1
    {whereIf}
ORDER BY c.ac_code
";
            return (await _appSqlServerRepository.QueryAsync<ChargeCodeOptionOutput>(sql, dp)).ToList();
        }

        public async Task<List<BranchOptionOutput>> BranchOptions(string query)
        {
            var dp = new DynamicParameters();
            var whereIf = "";

            if (!string.IsNullOrWhiteSpace(query))
            {
                whereIf += " AND (b.gb_code LIKE @kw OR b.gb_branchname LIKE @kw) ";
                dp.Add("kw", $"%{query.Trim()}%");
            }

            var sql = $@"
SELECT TOP 100
    b.gb_pk         AS pk,
    b.gb_code       AS code,
    b.gb_branchname AS [desc]
FROM GlbBranch b
WHERE b.gb_isactive = 1
    AND b.gb_isvalid = 1
    {whereIf}
ORDER BY b.gb_code
";
            return (await _appSqlServerRepository.QueryAsync<BranchOptionOutput>(sql, dp)).ToList();
        }

        public async Task<List<GstRateOptionOutput>> GstRateOptions(string query)
        {
            var dp = new DynamicParameters();
            var whereIf = "";
            if (!string.IsNullOrWhiteSpace(query))
            {
                whereIf += " AND (t.AT_Code LIKE @kw OR t.AT_Description LIKE @kw) ";
                dp.Add("kw", $"%{query.Trim()}%");
            }

            var sql = $@"
SELECT TOP 100
    t.AT_PK          AS pk,
    t.AT_Code        AS code,
    t.AT_Description AS [desc]
FROM AccTaxRate t
WHERE t.AT_IsActive = 1
    {whereIf}
ORDER BY t.AT_Code
";
            return (await _appSqlServerRepository.QueryAsync<GstRateOptionOutput>(sql, dp)).ToList();
        }

        public async Task<List<WhtRateOptionOutput>> WhtRateOptions(string query)
        {
            var dp = new DynamicParameters();
            var whereIf = "";
            if (!string.IsNullOrWhiteSpace(query))
            {
                whereIf += " AND (w.AW_Code LIKE @kw OR w.AW_Description LIKE @kw) ";
                dp.Add("kw", $"%{query.Trim()}%");
            }

            var sql = $@"
SELECT TOP 100
    w.AW_PK          AS pk,
    w.AW_Code        AS code,
    w.AW_Description AS [desc],
    w.AW_Rate        AS rate
FROM AccWithholding w
WHERE w.AW_IsActive = 1
    {whereIf}
ORDER BY w.AW_Code
";
            return (await _appSqlServerRepository.QueryAsync<WhtRateOptionOutput>(sql, dp)).ToList();
        }

        public async Task<List<VatClassOptionOutput>> VatClassOptions(string query)
        {
            var dp = new DynamicParameters();
            var whereIf = "";
            if (!string.IsNullOrWhiteSpace(query))
            {
                whereIf += " AND (m.A9_Code LIKE @kw OR m.A9_Description LIKE @kw) ";
                dp.Add("kw", $"%{query.Trim()}%");
            }

            var sql = $@"
SELECT TOP 100
    m.A9_PK          AS pk,
    m.A9_Code        AS code,
    m.A9_Description AS [desc]
FROM AccInvMsg m
WHERE m.A9_IsActive = 1
    {whereIf}
ORDER BY m.A9_Code
";
            return (await _appSqlServerRepository.QueryAsync<VatClassOptionOutput>(sql, dp)).ToList();
        }

        public async Task<string> GetHomeCurrency()
        {
            // snt 登录无 用户→分公司 映射，按"第一家启用公司"的本位币返回。
            var sql = @"
SELECT TOP 1 gc.gc_rx_nklocalcurrency
FROM GlbCompany gc
WHERE gc.gc_isactive = 1
    AND gc.gc_isvalid = 1
    AND gc.gc_rx_nklocalcurrency IS NOT NULL
ORDER BY gc.gc_code
";
            return await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(sql);
        }

        // ============================================================================
        // 写操作（新增 / 修改 / 生成草稿 / 过账 / 作废）
        // 实现全部在 BillingCore（一份代码给 shipment 与 合单共用），这里只固定 shipment 作用域。
        // ============================================================================

        public Task<BillingCreateOrUpdateOutput> CreateOrUpdate(BillingCreateInput input) =>
            _billingCore.CreateOrUpdateAsync(BillingScope.Shipment, input?.shpPk, input?.charges);

        public Task<List<string>> GenerateDraft(GenerateDraftInput input) =>
            _billingCore.GenerateDraftAsync(BillingScope.Shipment, input?.pks, input?.chargeType);

        public Task<int> PostCharge(PostChargeInput input) =>
            _billingCore.PostChargeAsync(BillingScope.Shipment, input?.pks, input?.chargeType);

        public Task<int> Delete(List<string> jrPks) =>
            _billingCore.DeleteAsync(jrPks);

        public Task<int> VoidDraftInvoice(VoidInvoiceInput input) =>
            _billingCore.VoidDraftInvoiceAsync(input?.ahPks);

        /// <summary>作废正式账单：走库内既有的 CRD 冲销流程（见 BillingCore.BuildCreditNoteSql）。</summary>
        public Task<int> VoidPostedInvoice(List<string> invoiceNos, string reason = null, string reasonDesc = null) =>
            _billingCore.VoidPostedInvoiceAsync(invoiceNos, reason, reasonDesc);

        public Task<int> EditDraftInvoice(DraftInvoiceEditInput input) =>
            _billingCore.EditDraftInvoiceAsync(input);

        /// <summary>
        /// 发票打印：按发票号逐张装配渲染模型 → 拼 HTML → Playwright 出 PDF，
        /// 文件落在 {wwwroot}/files/pdf/{yyyyMM}/ 下，返回相对访问路径。
        /// </summary>
        public async Task<GenerateInvoicePdfOutput> GenerateInvoicePdf(GenerateInvoicePdfInput input)
        {
            var invoiceNos = (input?.invoice_nos ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (invoiceNos.Count == 0)
            {
                throw new AppException("invoice_nos cannot be empty.");
            }

            var pdfRootFolder = string.IsNullOrWhiteSpace(_appFolders?.FilePdfFolder)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "files", "pdf")
                : _appFolders.FilePdfFolder;

            var output = new GenerateInvoicePdfOutput();
            var now = DateTime.Now;

            foreach (var invoiceNo in invoiceNos)
            {
                var model = await _invoicePdfDataProvider.BuildAsync(invoiceNo, input.ledger_type);
                if (model == null)
                {
                    throw new AppException($"invoice not found: {invoiceNo}.");
                }

                var html = InvoicePdfTemplateFactory.BuildHtml(model);
                var storage = PdfStoragePathBuilder.Build(pdfRootFolder, "INV", invoiceNo, now);
                await _playwrightPdfGenerator.GenerateAsync(html, storage.FullPath);

                output.results.Add(new InvoicePdfResult
                {
                    invoice_no = invoiceNo,
                    pdf_path = storage.OutputRelativePath
                });
            }

            return output;
        }
    }
}
