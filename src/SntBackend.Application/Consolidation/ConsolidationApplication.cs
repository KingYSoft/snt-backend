using Dapper;
using SntBackend.Application.Consolidation.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Consolidation
{
    public class ConsolidationApplication : SntBackendApplicationBase, IConsolidationApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public ConsolidationApplication(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

        private static string TblBuildWhere(List<ConsolidationTblFilterItem> filters, DynamicParameters dp)
        {
            var parts = new List<string>();

            static string MapOp(string op)
            {
                return op switch
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
            }

            foreach (var item in filters)
            {
                if (string.IsNullOrWhiteSpace(item.key))
                {
                    continue;
                }

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
                    if (string.IsNullOrWhiteSpace(item.val))
                    {
                        continue;
                    }
                    var val = item.val.Trim();
                    var paramName = $"@p{dp.ParameterNames.Count()}";
                    var isContain = item.op == "Contain" || item.op == "Not Contain";
                    parts.Add($" AND t.{item.key} {MapOp(item.op)} {paramName}");
                    dp.Add(paramName, isContain ? $"%{val}%" : val);
                }
            }

            return string.Join(" ", parts);
        }

        public async Task<ConsolidationTblOutput> Tbl(ConsolidationTblInput input)
        {
            var output = new ConsolidationTblOutput();
            var dp = new DynamicParameters();
            var whereIf = TblBuildWhere(input.filters, dp);

            var totalSql = @$"
SELECT COUNT(*)
FROM JobConsol t
WHERE 1 = 1
    AND t.jk_iscancelled = 0
    {whereIf}
";
            var pageSql = @$"
SELECT t.*
FROM JobConsol t
WHERE 1 = 1
    AND t.jk_iscancelled = 0
    {whereIf}
ORDER BY t.jk_pk desc
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
                var list = (await multi.ReadAsync<JobConsolDtoOutput>()).ToList();

                output.TotalCount = total;
                output.Items = list;
            }

            return output;
        }

        public async Task<ConsolidationDetailOutput> Detail(string id)
        {
            try
            {
                var dp = new DynamicParameters();
                dp.Add("id", id);

                var sql = @"
SELECT t.*
FROM JobConsol t
WHERE t.jk_pk = @id;

SELECT t.e2_pk, t.e2_isvalid, t.e2_addresstype, t.e2_isresidential, t.e2_addresssequence,
       t.e2_oa_address, t.e2_contact, t.e2_addressoverride,
       t.e2_address1, t.e2_address2, t.e2_city, t.e2_postcode, t.e2_state,
       t.e2_rn_nkcountrycode, t.e2_phone, t.e2_mobile, t.e2_fax,
       t.e2_govregnum, t.e2_govregnumtype, t.e2_email,
       t.e2_parentid, t.e2_parenttablecode, t.e2_validationstatus, t.e2_addressmap,
       t.e2_suppressaddressvalidationerror,
       t.e2_systemcreatetimeutc, t.e2_systemcreateuser,
       t.e2_systemlastedittimeutc, t.e2_systemlastedituser,
       CAST(t.e2_geolocation AS NVARCHAR(MAX)) AS e2_geolocation,
       t.e2_additionaladdressinformation, t.e2_companyname,
       t.e2_screeningstatus, t.e2_autoversion
FROM JobDocAddress t
WHERE t.e2_parentid = @id
    AND t.e2_addresstype IN ('CEC', 'CIC');

SELECT o.*
FROM OrgAddress o
WHERE o.OA_PK IN (
    SELECT t.e2_oa_address
    FROM JobDocAddress t
    WHERE t.e2_parentid = @id
        AND t.e2_addresstype IN ('CEC', 'CIC')
);

SELECT t.*
FROM JobConsolTransport t
WHERE t.jw_parentguid = @id
    AND t.jw_parenttype = 'CON'
ORDER BY t.jw_legorder;

SELECT s.*
FROM JobShipment s
INNER JOIN JobConShipLink l ON l.jn_js = s.js_pk
WHERE l.jn_jk = @id;

SELECT t.*
FROM JobContainer t
WHERE t.jc_jk = @id
    AND t.jc_isvalid = 0
";

                using (var multi = await _appSqlServerRepository.QueryMultipleAsync(sql, dp))
                {
                    var details = (await multi.ReadAsync<ConsolidationDetailOutput>()).ToList();
                    var detail = details.FirstOrDefault();
                    if (detail == null) return null;

                    var agents = (await multi.ReadAsync<ConsolidationAgentOutput>()).ToList();
                    var orgAddresses = (await multi.ReadAsync<OrgAddressDtoOutput>()).ToList();

                    detail.local_agent = agents.FirstOrDefault(a => a.e2_addresstype == "CEC");
                    detail.overseas_agent = agents.FirstOrDefault(a => a.e2_addresstype == "CIC");

                    if (detail.local_agent != null)
                        detail.local_agent.org_address = orgAddresses.FirstOrDefault(o => o.oa_pk == detail.local_agent.e2_oa_address);
                    if (detail.overseas_agent != null)
                        detail.overseas_agent.org_address = orgAddresses.FirstOrDefault(o => o.oa_pk == detail.overseas_agent.e2_oa_address);

                    if (!multi.IsConsumed)
                        detail.transport_list =
                            (await multi.ReadAsync<JobConsolTransportDtoOutput>()).ToList();

                    if (!multi.IsConsumed)
                        detail.shps = (await multi.ReadAsync<JobShipmentDtoOutput>()).ToList();

                    if (!multi.IsConsumed)
                        detail.containers = (await multi.ReadAsync<JobContainerDtoOutput>()).ToList();

                    return detail;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConsolidationDetail Error] {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }
    }
}
