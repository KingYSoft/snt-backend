using Dapper;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.DomainService.Share.App;
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
ORDER BY t.ah_invoicedate desc, t.Id desc
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
FROM AccTransactionHeader t
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_fullypaiddate IS NOT NULL
    AND t.ah_iscancelled = 0
    AND t.ah_transactiontype IN ('REC', 'PAY')
    {whereIf}
";
            var pageSql = @$"
SELECT t.*, o.oh_fullname AS CompanyName
FROM AccTransactionHeader t
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_fullypaiddate IS NOT NULL
    AND t.ah_iscancelled = 0
    AND t.ah_transactiontype IN ('REC', 'PAY')
    {whereIf}
ORDER BY t.ah_fullypaiddate DESC, t.AH_PK DESC
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

        public async Task<WriteOffDetailOutput> WriteOffDetail(string id)
        {
            var dp = new DynamicParameters();
            dp.Add("id", id);

            var headerSql = @"
SELECT t.*, o.oh_fullname AS CompanyName
FROM AccTransactionHeader t
LEFT JOIN OrgHeader o ON o.OH_PK = t.ah_oh
WHERE t.ah_pk = @id
";
            var header = await _appSqlServerRepository.QueryFirstOrDefaultAsync<WriteOffDetailOutput>(headerSql, dp);
            if (header == null) return null;

            var matchSql = @"
SELECT m.*
FROM AccTransactionMatchLink m
WHERE m.ap_ah = @ahPk
ORDER BY m.ap_matchdate DESC
";
            var mdp = new DynamicParameters();
            mdp.Add("ahPk", header.ah_pk);
            var matchLinks = (await _appSqlServerRepository.QueryAsync<AccTransactionMatchLinkDtoOutput>(matchSql, mdp)).ToList();
            header.MatchLinks = matchLinks;

            return header;
        }
    }
}
