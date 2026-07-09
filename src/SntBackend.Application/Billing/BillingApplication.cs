using Dapper;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Billing.Dto.MatchTransaction;
using SntBackend.Application.Po.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Billing
{
    public class BillingApplication : SntBackendApplicationBase, IBillingApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public BillingApplication(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

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
    AND t.ah_transactiontype IN ('REC', 'PAY')
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
    AND t.ah_transactiontype IN ('REC', 'PAY')
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
    AND t.ah_transactiontype IN ('REC', 'PAY')
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
    AND t.ah_transactiontype IN ('REC', 'PAY')
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

        public async Task<BillingChargeLineOutput> QueryChargeLine(BillingChargeLineInput input)
        {
            if (string.IsNullOrWhiteSpace(input?.shpPk))
                throw new System.Exception("shpPk cannot be empty.");
            if (string.IsNullOrWhiteSpace(input.chargeType))
                throw new System.Exception("chargeType cannot be empty.");

            var isAr = string.Equals(input.chargeType, "AR", System.StringComparison.OrdinalIgnoreCase);
            var isAp = string.Equals(input.chargeType, "AP", System.StringComparison.OrdinalIgnoreCase);
            if (!isAr && !isAp)
                throw new System.Exception("chargeType must be AR or AP.");

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

            var orderBy = !string.IsNullOrWhiteSpace(input.Sorting) &&
                          input.Sorting.IndexOf("DESC", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? "ORDER BY jr.jr_displaysequence DESC, jr.jr_pk DESC"
                : "ORDER BY jr.jr_displaysequence, jr.jr_pk";

            var dp = new DynamicParameters();
            dp.Add("shpPk", input.shpPk);
            dp.Add("skipCount", input.SkipCount);
            dp.Add("takeCount", input.MaxResultCount);

            var totalSql = $@"
SELECT COUNT(*)
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
INNER JOIN JobShipment js ON js.js_pk = jh.jh_parentid
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND js.js_iscancelled = 0
    AND jr.jr_isvalid = 1
    AND {sideFilter}
";
            var pageSql = $@"
SELECT
    jr.jr_pk,
    jr.jr_jh,
    jr.jr_chargetype,
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
    {lineCol}      AS line_pk,
    inv.ah_pk              AS invoice_pk,
    inv.ah_transactionnum  AS invoice_no,
    inv.ah_invoicedate     AS invoice_date,
    -- 已过账(ah_postdate 有值)= N；未链接或仍是草稿(postdate 为空)= Y
    CASE WHEN inv.ah_postdate IS NOT NULL THEN 'N' ELSE 'Y' END AS Draft
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
INNER JOIN JobShipment js ON js.js_pk = jh.jh_parentid
LEFT JOIN AccTransactionLines line ON line.al_pk = {lineCol}
LEFT JOIN AccTransactionHeader inv ON inv.ah_pk = line.al_ah AND inv.ah_iscancelled = 0
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND js.js_iscancelled = 0
    AND jr.jr_isvalid = 1
    AND {sideFilter}
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

        public async Task<BillingDraftPageOutput> QueryDraftPage(BillingDraftPageInput input)
        {
            if (string.IsNullOrWhiteSpace(input?.shpPk))
                throw new System.Exception("shpPk cannot be empty.");

            var orderBy = !string.IsNullOrWhiteSpace(input.Sorting) &&
                          input.Sorting.IndexOf("DESC", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? "ORDER BY ah.ah_invoicedate DESC, ah.ah_pk DESC"
                : "ORDER BY ah.ah_invoicedate, ah.ah_pk";

            var dp = new DynamicParameters();
            dp.Add("shpPk", input.shpPk);
            dp.Add("skipCount", input.SkipCount);
            dp.Add("takeCount", input.MaxResultCount);

            var ledgerWhere = "";
            if (!string.IsNullOrWhiteSpace(input.chargeType))
            {
                ledgerWhere = " AND ah.ah_ledger = @chargeType ";
                dp.Add("chargeType", input.chargeType);
            }

            var totalSql = $@"
SELECT COUNT(*)
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
INNER JOIN JobShipment js ON js.js_pk = jh.jh_parentid
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND js.js_iscancelled = 0
    AND ah.ah_iscancelled = 0
    AND ah.ah_transactiontype = 'INV'
    {ledgerWhere}
";
            var pageSql = $@"
SELECT ah.*
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
INNER JOIN JobShipment js ON js.js_pk = jh.jh_parentid
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND js.js_iscancelled = 0
    AND ah.ah_iscancelled = 0
    AND ah.ah_transactiontype = 'INV'
    {ledgerWhere}
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

        public async Task<BillingSummaryDto> GetBillingSummary(string shpPk)
        {
            if (string.IsNullOrWhiteSpace(shpPk))
                throw new System.Exception("shpPk cannot be empty.");

            var dp = new DynamicParameters();
            dp.Add("shpPk", shpPk);

            var sql = @"
SELECT
    ISNULL(SUM(jr.jr_localsellamt), 0) AS ar,
    ISNULL(SUM(jr.jr_localcostamt), 0) AS ap
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
INNER JOIN JobShipment js ON js.js_pk = jh.jh_parentid
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
    AND js.js_iscancelled = 0
    AND jr.jr_isvalid = 1
";
            var row = await _appSqlServerRepository.QueryFirstOrDefaultAsync<(decimal ar, decimal ap)>(sql, dp);

            var ar = row.ar;
            var ap = row.ap;
            var profits = ar - ap;
            var grossProfitMargin = ar > 0 ? profits / ar * 100 : 0;

            return new BillingSummaryDto
            {
                ar = System.Math.Round(ar, 2),
                ap = System.Math.Round(ap, 2),
                profits = System.Math.Round(profits, 2),
                grossProfitMargin = System.Math.Round(grossProfitMargin, 2),
                home_currency = ""
            };
        }

        public async Task<QueryChargesByInvoiceOutput> QueryChargesByInvoiceNo(string invoiceNo)
        {
            if (string.IsNullOrWhiteSpace(invoiceNo))
                throw new System.Exception("invoiceNo cannot be empty.");

            var dp = new DynamicParameters();
            dp.Add("invoiceNo", invoiceNo);

            var headSql = @"
SELECT TOP 1 ah.*
FROM AccTransactionHeader ah
WHERE (ah.ah_transactionnum = @invoiceNo OR ah.ah_consolidatedinvoiceref = @invoiceNo)
    AND ah.ah_iscancelled = 0
ORDER BY ah.ah_invoicedate DESC, ah.ah_pk DESC
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
                var chargesDp = new DynamicParameters();
                chargesDp.Add("linePks", linePks);
                chargesDp.Add("ahPk", head.ah_pk);

                var chargesSql = $@"
SELECT
    jr.jr_pk,
    jr.jr_jh,
    jr.jr_chargetype,
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
        // 写操作（新增 / 修改 / 生成草稿 / 过账）
        // 移植自 first-cargo-backend IBillingApplication，按 snt 表名/字段名重写。
        //
        // 重要差异（snt 与 first-cargo 数据模型不同）：
        //  1. snt 无草稿表(TTL_TEMP_LINES)。"草稿"与"已过账"统一落在 AccTransactionHeader/Lines，
        //     以 ah_postdate 是否为空区分：NULL=草稿，有值=已过账。
        //  2. snt 的 JobCharge 是 BTH，一行同时含 AR(sell)/AP(cost) 两侧，
        //     这里按 chargeType 只写所选侧；jr_al_arline/jr_al_apline 为该侧已开票链接。
        //  3. branch/company/dept 等 create-time 字段从该 shipment 下的 JobHeader 继承。
        //  4. 大表 INSERT 采用 “复制模板行 + 覆盖业务列” 方式（同 SaveMatchWriteOff），
        //     以满足众多 NOT NULL 列。
        //  ⚠ AP 侧金额按 snt 习惯写为负数（与现有 AP 数据一致），如与实际数据不符可调整 sign。
        // ============================================================================

        private const string SysUser = "system";

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

        public async Task<BillingCreateOrUpdateOutput> CreateOrUpdate(BillingCreateInput input)
        {
            if (string.IsNullOrWhiteSpace(input?.shpPk))
                throw new Exception("shpPk cannot be empty.");
            if (input.charges == null || input.charges.Count == 0)
                throw new Exception("charges cannot be empty.");

            // 作废判断以 JobShipment.js_iscancelled 为准（不是 JobHeader.jh_isvalid）
            var shpDp = new DynamicParameters();
            shpDp.Add("shpPk", input.shpPk);
            var cancelled = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int?>(
                "SELECT js_iscancelled FROM JobShipment WHERE js_pk = @shpPk", shpDp);
            if (cancelled == null)
                throw new Exception("货运不存在。");
            if (cancelled == 1)
                throw new Exception("该货运已取消，无法添加费用。");

            // 解析目标 JobHeader（该 shipment 下的作业头，优先有效的），create-time 字段从它继承
            var jobDp = new DynamicParameters();
            jobDp.Add("shpPk", input.shpPk);
            var job = await _appSqlServerRepository.QueryFirstOrDefaultAsync<JobHeaderCtx>(@"
SELECT TOP 1 jh.jh_pk, jh.jh_gb, jh.jh_gc, jh.jh_ge, jh.jh_jobnum
FROM JobHeader jh
WHERE jh.jh_parentid = @shpPk
    AND jh.jh_parenttablecode = 'JS'
ORDER BY jh.jh_isvalid DESC, jh.jh_pk
", jobDp);

            if (job == null || string.IsNullOrWhiteSpace(job.jh_pk))
                throw new Exception("未找到该货运对应的作业头。");

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

            foreach (var c in input.charges)
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
                var gst = c.gst_rate;
                var wht = c.wht_rate;
                var vat = c.vat_class;
                // AP 按负数存储（与 snt 现有 AP 数据一致）
                if (isAp)
                {
                    os = -Math.Abs(os);
                    local = -Math.Abs(local);
                }

                var p = new DynamicParameters();
                p.Add("code", c.jr_chargetype);
                p.Add("desc", c.jr_desc);
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
                    p.Add("gb", job.jh_gb);
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
                    var setSide = isAr
                        ? @"jr_oh_sellaccount=@sellParty, jr_rx_nksellcurrency=@sellCcy, jr_ossellexrate=@sellRate,
                            jr_ossellamt=@osSell, jr_localsellamt=@localSell, jr_at_sellgstrate=@sellGst,
                            jr_aw_sellwhtrate=@sellWht, jr_a9_sellvatclass=@sellVat"
                        : @"jr_oh_costaccount=@costParty, jr_rx_nkcostcurrency=@costCcy, jr_oscostexrate=@costRate,
                            jr_oscostamt=@osCost, jr_localcostamt=@localCost, jr_at_costgstrate=@costGst,
                            jr_aw_costwhtrate=@costWht, jr_a9_costvatclass=@costVat";

                    var rows = await _appSqlServerRepository.ExecuteAsync($@"
UPDATE JobCharge SET
    jr_chargetype = @code,
    jr_desc = @desc,
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
    @pk, 1, @jh, @ge, @gb, t.jr_jh_internaljob, t.jr_ge_internaldept, t.jr_gb_internalbranch,
    t.jr_linecfx, t.jr_ac, @desc, @costParty, t.jr_costrated, t.jr_costratingoverride, @osCost,
    0, @localCost, @costCcy, @costRate, @costGst,
    0, @costVat, @costWht, 0, 0,
    NULL, NULL, NULL, NULL, 0,
    NULL, NULL, NULL, NULL, NULL, NULL, 0, t.jr_proformacost,
    @sellParty, NULL, NULL, @sellCcy, @sellRate,
    @osSell, @sellVat, 0, @localSell, t.jr_sellrated, t.jr_sellratingoverride,
    @sellGst, @sellWht, 0, NULL, NULL, NULL,
    0, t.jr_isincludedinprofitshare, @code, 0, 0,
    t.jr_invoicetype, t.jr_proformarevenue, 0, @seq, NULL,
    NULL, t.jr_op_product, t.jr_productquantity, t.jr_e6, @gc, NULL, t.jr_e6_gatewaysellheader,
    NULL, @ledger, @sellCcy, NULL, NULL, NULL,
    0, NULL, t.jr_autoversion, t.jr_costplaceofsupply, t.jr_costplaceofsupplytype,
    t.jr_sellplaceofsupply, t.jr_sellplaceofsupplytype, t.jr_costsupplytype, t.jr_sellsupplytype, @now,
    @user, NULL, @user, NULL, NULL,
    NULL, NULL, t.jr_isapcashadvance, t.jr_isarcashadvance, t.jr_isspotcost,
    NULL, NULL
FROM JobCharge t
WHERE t.jr_pk = @templatePk
";

        public async Task<List<string>> GenerateDraft(GenerateDraftInput input)
        {
            if (input?.pks == null || input.pks.Count == 0)
                throw new Exception("pks cannot be empty.");
            var isAr = string.Equals(input.chargeType, "AR", StringComparison.OrdinalIgnoreCase);
            var isAp = string.Equals(input.chargeType, "AP", StringComparison.OrdinalIgnoreCase);
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

            // 加载选中且该侧尚未开票(link IS NULL)的费用 + 所属作业/运单上下文
            var loadDp = new DynamicParameters();
            loadDp.Add("pks", input.pks);
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
    jh.jh_parentid  AS shp_pk,
    jh.jh_jobnum    AS jh_jobnum,
    jh.jh_gb        AS jh_gb,
    jh.jh_gc        AS jh_gc,
    jh.jh_ge        AS jh_ge
FROM JobCharge jr
INNER JOIN JobHeader jh ON jh.jh_pk = jr.jr_jh
WHERE jr.jr_pk IN @pks
    AND {sideLink} IS NULL
", loadDp)).ToList();

            if (charges.Count == 0)
                return new List<string>();

            var shpPk = charges[0].shp_pk;

            // 发票号基础串：运单 consign ref
            var refDp = new DynamicParameters();
            refDp.Add("shpPk", shpPk);
            var consignRef = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 js_uniqueconsignref FROM JobShipment WHERE js_pk = @shpPk", refDp);
            if (string.IsNullOrWhiteSpace(consignRef))
                consignRef = charges[0].jh_jobnum ?? "INV";

            // 该运单下已有发票号，算下一个后缀
            var existDp = new DynamicParameters();
            existDp.Add("shpPk", shpPk);
            var existingNos = (await _appSqlServerRepository.QueryAsync<string>(@"
SELECT ah.ah_transactionnum
FROM AccTransactionHeader ah
INNER JOIN JobHeader jh ON jh.jh_pk = ah.ah_jh
WHERE jh.jh_parentid = @shpPk AND jh.jh_parenttablecode = 'JS'
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
            var createdInvoiceNos = new List<string>();

            // 按 结算单位 + 币种 分组，每组一个草稿发票
            var groups = charges.GroupBy(x => new { Party = x.party_oh ?? "", Ccy = x.currency ?? "" });

            foreach (var g in groups)
            {
                var items = g.OrderBy(x => x.jr_displaysequence).ToList();
                var invNo = $"{consignRef}/{GenerateInvoiceSuffix(++maxIndex)}";
                createdInvoiceNos.Add(invNo);

                var ahPk = Guid.NewGuid().ToString();
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
                    var statusCol = isAr ? "jr_arlinepostingstatus" : "jr_aplinepostingstatus";
                    await _appSqlServerRepository.ExecuteAsync(
                        $"UPDATE JobCharge SET {linkCol} = @alpk, {statusCol} = 'draft' WHERE jr_pk = @jrpk", linkDp);
                }
            }

            return createdInvoiceNos;
        }

        public async Task<int> PostCharge(PostChargeInput input)
        {
            if (input?.ahPks == null || input.ahPks.Count == 0)
                throw new Exception("ahPks cannot be empty.");

            var now = DateTime.UtcNow;
            var posted = 0;

            foreach (var ahPk in input.ahPks.Distinct())
            {
                if (string.IsNullOrWhiteSpace(ahPk)) continue;

                var hDp = new DynamicParameters();
                hDp.Add("ah", ahPk);
                var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<HeaderPostRow>(
                    "SELECT ah_pk, ah_ledger, ah_postdate FROM AccTransactionHeader WHERE ah_pk = @ah", hDp);
                if (header == null) continue;
                if (header.ah_postdate != null) continue; // 已过账，跳过

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

                // 3. 关联 JobCharge 过账状态置 posted（按 ledger 决定侧别）
                var isAr = string.Equals(header.ah_ledger, "AR", StringComparison.OrdinalIgnoreCase);
                var linkCol = isAr ? "jr_al_arline" : "jr_al_apline";
                var statusCol = isAr ? "jr_arlinepostingstatus" : "jr_aplinepostingstatus";
                await _appSqlServerRepository.ExecuteAsync($@"
UPDATE JobCharge SET {statusCol} = 'posted', jr_systemlastedittimeutc = @now
WHERE {linkCol} IN (SELECT al_pk FROM AccTransactionLines WHERE al_ah = @ah)", upDp);

                posted++;
            }

            return posted;
        }

        public async Task<int> Delete(List<string> jrPks)
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

        public async Task<int> VoidDraftInvoice(VoidInvoiceInput input)
        {
            if (input?.ahPks == null || input.ahPks.Count == 0)
                throw new Exception("ahPks cannot be empty.");

            var now = DateTime.UtcNow;
            var affected = 0;

            foreach (var ahPk in input.ahPks.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
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

        public async Task<int> VoidPostedInvoice(List<string> invoiceNos)
        {
            if (invoiceNos == null || invoiceNos.Count == 0)
                throw new Exception("invoiceNos cannot be empty.");

            var now = DateTime.UtcNow;
            var affected = 0;

            foreach (var no in invoiceNos.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct())
            {
                var hDp = new DynamicParameters();
                hDp.Add("no", no);
                var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<HeaderVoidRow>(@"
SELECT ah_pk, ah_ledger, ah_postdate, ah_outstandingamount, ah_invoiceamount
FROM AccTransactionHeader
WHERE ah_transactionnum = @no AND ah_iscancelled = 0 AND ah_postdate IS NOT NULL", hDp);
                if (header == null) continue;

                // 已有核销/付款（未结金额 != 发票金额）不允许作废
                if (Math.Abs(header.ah_outstandingamount) < Math.Abs(header.ah_invoiceamount))
                    throw new Exception($"Cannot void matched/paid invoice: {no}.");

                await UnlinkChargesByHeaderAsync(header.ah_pk, header.ah_ledger, now);

                var cDp = new DynamicParameters();
                cDp.Add("ah", header.ah_pk);
                cDp.Add("now", now);
                await _appSqlServerRepository.ExecuteAsync(
                    "UPDATE AccTransactionHeader SET ah_iscancelled = 1, ah_systemlastedittimeutc = @now WHERE ah_pk = @ah", cDp);
                affected++;
            }

            return affected;
        }

        public async Task<int> EditDraftInvoice(DraftInvoiceEditInput input)
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

                        // 回填链接
                        await _appSqlServerRepository.ExecuteAsync(
                            $"UPDATE JobCharge SET {linkCol} = @al, {statusCol} = 'draft' WHERE jr_pk = @jr",
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
UPDATE JobCharge SET {linkCol} = NULL, {statusCol} = NULL, jr_systemlastedittimeutc = @now
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
            p.Add("ledger", isAr ? "AR" : "AP");
            p.Add("seq", seq);
            p.Add("now", now);
            p.Add("user", SysUser);
            p.Add("templatePk", templatePk);
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

        // INSERT AccTransactionHeader（草稿：ah_postdate = NULL）；列顺序同 SaveMatchWriteOff
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
    t.ah_transactioncategory, NULL, t.ah_receipttype,
    t.ah_cashbasisgstindicator, t.ah_cashbasisgstrealisedtogl,
    t.ah_chequedrawer, t.ah_drawerbank, t.ah_drawerbranch,
    0, NULL, NULL,
    0, 0, NULL,
    0, @amt, 0, t.ah_posttogl,
    NULL, 0,
    t.ah_invoiceterm, t.ah_invoicetermdays, NULL, t.ah_requisitionstatus,
    0, 0, 0,
    0, 0, 0, 0,
    NULL, @oh, NULL, NULL,
    @jh, @gb, @gc, @ge, NULL,
    t.ah_transactionbelongstogroup, NULL,
    @now, @user,
    NULL, @user,
    t.ah_agreedpaymentmethodoverride, NULL,
    NULL, NULL, NULL,
    0, 0, t.ah_autoversion,
    NULL, NULL, NULL,
    NULL, NULL,
    t.ah_placeofsupply, t.ah_placeofsupplytype, t.ah_xd_compliancebook,
    @amt, @jobnum,
    NULL, NULL,
    t.ah_gb_taxbranch, NULL, NULL,
    0, @ostotal, t.ah_overrideexchangerate,
    @gb, @ge
FROM AccTransactionHeader t
WHERE t.ah_pk = @templatePk
";

        // INSERT AccTransactionLines（草稿：al_postdate = NULL）；列顺序同 Po 实体
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
    @alpk, @altype, @seq, @desc, @localamt, @gstcode, @gst, NULL,
    @vat, @whtcode, 0, 1, @osamt, @osamt, @osamt,
    @ccy, @rate, 0, t.al_postperiod, NULL,
    t.al_posttogl, t.al_reverseperiod, NULL, t.al_reversetogl, 0,
    0, 0, 0, t.al_revrecognitiontype, @jh,
    t.al_ac, @ge, @gb, t.al_ag, @oh, t.al_ag_percentof, 0, @ah, @gc,
    @now, @user, NULL, @user,
    NULL, 0, NULL, 0, 0,
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
            public string shp_pk { get; set; }
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
