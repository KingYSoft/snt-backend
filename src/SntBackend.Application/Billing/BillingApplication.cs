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
SELECT t.*
FROM AccTransactionHeader t
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
SELECT t.*
FROM AccTransactionHeader t
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

            var skipCount = (input.PageIndex - 1) * input.PageSize;
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
SELECT t.ah_pk, t.ah_ledger, t.ah_transactiontype, t.ah_transactionnum,
       t.ah_outstandingamount, t.ah_ostotal, t.ah_exchangerate,
       t.ah_rx_nktransactioncurrency, t.ah_oh, t.ah_jh, t.ah_gb, t.ah_gc, t.ah_ge,
       t.ah_iscancelled, t.ah_invoiceamount
FROM AccTransactionHeader t
WHERE t.ah_pk = @pk
";
                var invoice = await _appSqlServerRepository.QueryFirstOrDefaultAsync<dynamic>(invSql, invDp);
                if (invoice == null) continue;

                // 校验
                string invType = invoice.ah_transactiontype;
                if (invType != "INV" && invType != "BILL") continue;
                if ((int)invoice.ah_iscancelled != 0) continue;

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

                // 新建 REC/PAY Header
                var newPk = Guid.NewGuid().ToString();
                var description = string.IsNullOrWhiteSpace(input.Description)
                    ? $"Match Write Off - {(string)invoice.ah_transactionnum}"
                    : input.Description;

                var insertHeaderSql = @"
INSERT INTO AccTransactionHeader (
    ah_pk, ah_ledger, ah_transactiontype, ah_transactionnum,
    ah_desc, ah_invoicedate, ah_duedate, ah_invoiceamount,
    ah_outstandingamount, ah_ostotal, ah_rx_nktransactioncurrency,
    ah_exchangerate, ah_oh, ah_jh, ah_gb, ah_gc, ah_ge,
    ah_fullypaiddate, ah_iscancelled, ah_matchstatus,
    ah_chequeorreference, ah_ab, ah_transactioncreatedbymatching,
    ah_systemcreatetimeutc, ah_systemcreateuser,
    ah_postdate
)
VALUES (
    @newPk, @ledger, @transType, @matchNumber,
    @desc, @settleDate, @settleDate, @amount,
    0, 0, @currency,
    @exRate, @oh, @jh, @gb, @gc, @ge,
    @settleDate, 0, 'Completed',
    @chequeNo, @bankPk, 1,
    @now, @user,
    @settleDate
)
";
                var headerDp = new DynamicParameters();
                headerDp.Add("newPk", newPk);
                headerDp.Add("ledger", invLedger);
                headerDp.Add("transType", transactionType);
                headerDp.Add("matchNumber", matchNumber);
                headerDp.Add("desc", description);
                headerDp.Add("settleDate", input.SettleDate.Value);
                headerDp.Add("amount", isReceipt ? writeOffHome : -writeOffHome);
                headerDp.Add("currency", (string)invoice.ah_rx_nktransactioncurrency);
                headerDp.Add("exRate", exRate);
                headerDp.Add("oh", (string)invoice.ah_oh);
                headerDp.Add("jh", (string)invoice.ah_jh);
                headerDp.Add("gb", (string)invoice.ah_gb);
                headerDp.Add("gc", (string)invoice.ah_gc);
                headerDp.Add("ge", (string)invoice.ah_ge);
                headerDp.Add("chequeNo", input.ChequeNo);
                headerDp.Add("bankPk", input.BankPK);
                headerDp.Add("now", DateTime.UtcNow);
                headerDp.Add("user", AbpSession.UserId?.ToString());

                await _appSqlServerRepository.ExecuteAsync(insertHeaderSql, headerDp);
                newHeaderPks.Add(newPk);

                // 新建 MatchLink
                var matchLinkPk = Guid.NewGuid().ToString();
                var insertMatchLinkSql = @"
INSERT INTO AccTransactionMatchLink (
    ap_pk, ap_ah, ap_amount, ap_osamount, ap_gstrealised,
    ap_matchdate, ap_matchgroupnum, ap_reason,
    ap_systemcreatetimeutc, ap_systemcreateuser
)
VALUES (
    @linkPk, @ahPk, @amount, @osAmount, 0,
    @matchDate, @matchGroupNum, @reason,
    @now, @user
)
";
                var linkDp = new DynamicParameters();
                linkDp.Add("linkPk", matchLinkPk);
                linkDp.Add("ahPk", newPk);
                linkDp.Add("amount", writeOffHome);
                linkDp.Add("osAmount", writeOffOriginal);
                linkDp.Add("matchDate", input.SettleDate.Value);
                linkDp.Add("matchGroupNum", matchNumber);
                linkDp.Add("reason", $"Match to {(string)invoice.ah_transactionnum}");
                linkDp.Add("now", DateTime.UtcNow);
                linkDp.Add("user", AbpSession.UserId?.ToString());

                await _appSqlServerRepository.ExecuteAsync(insertMatchLinkSql, linkDp);

                // 更新原发票 outstanding
                var newOutstanding = outstanding - (isReceipt ? writeOffHome : -writeOffHome);
                var newOsTotal = osTotal - (isReceipt ? writeOffOriginal : -writeOffOriginal);
                var isFullyPaid = Math.Abs(newOutstanding) < 0.01m;

                var updateInvSql = @"
UPDATE AccTransactionHeader
SET ah_outstandingamount = @newOutstanding,
    ah_ostotal = @newOsTotal,
    ah_matchstatus = @matchStatus,
    ah_fullypaiddate = CASE WHEN @isFullyPaid = 1 THEN @settleDate ELSE ah_fullypaiddate END,
    ah_systemlastedittimeutc = @now,
    ah_systemlastedituser = @user
WHERE ah_pk = @pk
";
                var updDp = new DynamicParameters();
                updDp.Add("newOutstanding", isFullyPaid ? 0m : newOutstanding);
                updDp.Add("newOsTotal", isFullyPaid ? 0m : newOsTotal);
                updDp.Add("matchStatus", isFullyPaid ? "Completed" : "Partial");
                updDp.Add("isFullyPaid", isFullyPaid ? 1 : 0);
                updDp.Add("settleDate", input.SettleDate.Value);
                updDp.Add("now", DateTime.UtcNow);
                updDp.Add("user", AbpSession.UserId?.ToString());
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
    t.ah_rx_nktransactioncurrency AS Currency,
    t.ah_invoiceamount AS SettledAmount,
    CONVERT(varchar(10), t.ah_invoicedate, 23) AS PaymentDate,
    t.ah_desc AS Description
FROM AccTransactionHeader t
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

        public async Task<MatchTransactionDetailOutput> MatchTransactionDetail(string pk)
        {
            var dp = new DynamicParameters();
            dp.Add("pk", pk);

            var headerSql = @"
SELECT t.*
FROM AccTransactionHeader t
WHERE t.ah_pk = @pk
";
            var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<AccTransactionHeaderDtoOutput>(headerSql, dp);
            if (header == null) return null;

            var linesSql = @"
SELECT l.*
FROM AccTransactionLines l
WHERE l.al_ah = @pk
ORDER BY l.al_sequence
";
            var lines = (await _appSqlServerRepository.QueryAsync<AccTransactionLinesDtoOutput>(linesSql, dp)).ToList();

            return new MatchTransactionDetailOutput
            {
                Header = header,
                Lines = lines
            };
        }
    }
}
