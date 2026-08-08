using Dapper;
using SntBackend.Application.Consolidation.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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

        private static bool IsShipmentField(string key) => key.StartsWith("js_");

        private static string GetColumnExpr(string key)
        {
            if (IsShipmentField(key))
                return $"s.{key}";
            return $"t.{key}";
        }

        private static string TblBuildWhere(List<ConsolidationTblFilterItem> filters, DynamicParameters dp, out bool joinShipment)
        {
            var parts = new List<string>();
            joinShipment = false;

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

                if (IsShipmentField(item.key))
                    joinShipment = true;

                var col = GetColumnExpr(item.key);

                if (item.op == "between")
                {
                    if (!string.IsNullOrWhiteSpace(item.start))
                    {
                        var paramNameStart = $"@p{dp.ParameterNames.Count()}";
                        parts.Add($" AND {col} >= {paramNameStart} ");
                        dp.Add(paramNameStart, item.start);
                    }
                    if (!string.IsNullOrWhiteSpace(item.end))
                    {
                        var paramNameEnd = $"@p{dp.ParameterNames.Count()}";
                        parts.Add($" AND {col} <= {paramNameEnd}");
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
                    parts.Add($" AND {col} {MapOp(item.op)} {paramName}");
                    dp.Add(paramName, isContain ? $"%{val}%" : val);
                }
            }

            return string.Join(" ", parts);
        }

        public async Task<ConsolidationTblOutput> Tbl(ConsolidationTblInput input)
        {
            var output = new ConsolidationTblOutput();
            var dp = new DynamicParameters();
            var whereIf = TblBuildWhere(input.filters, dp, out var joinShipment);

            var shipmentJoin = joinShipment
                ? "LEFT JOIN JobConShipLink l ON l.jn_jk = t.jk_pk LEFT JOIN JobShipment s ON s.js_pk = l.jn_js"
                : "";
            var distinct = joinShipment ? "DISTINCT" : "";

            var totalSql = @$"
SELECT COUNT({distinct} t.jk_pk)
FROM JobConsol t
{shipmentJoin}
WHERE 1 = 1
    AND t.jk_iscancelled = 0
    {whereIf}
";
            var pageSql = @$"
SELECT {distinct} t.*
FROM JobConsol t
{shipmentJoin}
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

        public async Task<byte[]> Export(ConsolidationTblInput input)
        {
            var dp = new DynamicParameters();
            var whereIf = TblBuildWhere(input.filters, dp, out var joinShipment);

            var shipmentJoin = joinShipment
                ? "LEFT JOIN JobConShipLink l ON l.jn_jk = t.jk_pk LEFT JOIN JobShipment s ON s.js_pk = l.jn_js"
                : "";
            var distinct = joinShipment ? "DISTINCT" : "";

            var sql = @$"
SELECT {distinct} t.*
FROM JobConsol t
{shipmentJoin}
WHERE 1 = 1
    AND t.jk_iscancelled = 0
    {whereIf}
ORDER BY t.jk_pk desc
";
            var list = (await _appSqlServerRepository.QueryAsync<JobConsolDtoOutput>(sql, dp)).ToList();

            var props = typeof(JobConsolDtoOutput).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var sb = new StringBuilder();

            // header
            sb.AppendLine(string.Join(",", props.Select(p => p.Name)));

            // rows
            foreach (var item in list)
            {
                var values = props.Select(p =>
                {
                    var val = p.GetValue(item);
                    if (val == null) return "";
                    var str = val is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm:ss") : val.ToString();
                    return str.Contains(',') || str.Contains('"') || str.Contains('\n')
                        ? $"\"{str.Replace("\"", "\"\"")}\""
                        : str;
                });
                sb.AppendLine(string.Join(",", values));
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
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

SELECT o.oa_pk, o.oa_isvalid, o.oa_isactive, o.oa_code, o.oa_companynameoverride,
       o.oa_address1, o.oa_address2, o.oa_city, o.oa_state, o.oa_postcode,
       o.oa_rn_nkcountrycode, o.oa_validationstatus, o.oa_addressmap,
       o.oa_phone, o.oa_fax, o.oa_mobile, o.oa_email,
       o.oa_usecumulativefreewaitingtime,
       o.oa_pickupfromtimeonly, o.oa_pickuptotimeonly,
       o.oa_deliverfromtimeonly, o.oa_delivertotimeonly,
       o.oa_donotattendfrom, o.oa_donotattendto,
       o.oa_dockleveler, o.oa_forklift, o.oa_palletjack,
       o.oa_containerhandling, o.oa_accesspoint, o.oa_labourrequired,
       o.oa_communicationrequired, o.oa_dock_height,
       o.oa_fclequipmentneeded, o.oa_lclequipmentneeded, o.oa_airequipmentneeded,
       o.oa_rl_nkrelatedportcode, o.oa_oh, o.oa_deliveryroute, o.oa_deliveryroutesequence,
       o.oa_otherwarehousefacilities, o.oa_loadingunloadingconstraints, o.oa_authoritytoleave,
       o.oa_groupnumber, o.oa_additionaladdressinformation,
       CAST(o.oa_geofencepolygon AS NVARCHAR(MAX)) AS oa_geofencepolygon,
       o.oa_suppressaddressvalidationerror, o.oa_verifiescontainergrossweight,
       o.oa_systemcreatetimeutc, o.oa_systemcreateuser,
       o.oa_systemlastedittimeutc, o.oa_systemlastedituser,
       o.oa_language,
       CAST(o.oa_geolocation AS NVARCHAR(MAX)) AS oa_geolocation,
       o.oa_jobloadingduration, o.oa_autoversion,
       oh.oh_fullname AS oh_fullname
FROM OrgAddress o
LEFT JOIN OrgHeader oh ON oh.oh_pk = o.oa_oh
WHERE o.OA_PK IN (
    SELECT t.e2_oa_address
    FROM JobDocAddress t
    WHERE t.e2_parentid = @id
        AND t.e2_addresstype IN ('CEC', 'CIC')
);

-- 路由：jw_parentguid 是合单 pk，展示要的是合单号；承运人要经 OrgAddress 才能拿到名称
SELECT t.*,
    jk.jk_uniqueconsignref AS consol_no,
    carrier.oh_fullname    AS carrier_name,
    carrier.oh_code        AS carrier_code
FROM JobConsolTransport t
LEFT JOIN JobConsol  jk         ON jk.jk_pk = t.jw_parentguid
LEFT JOIN OrgAddress carrier_oa ON carrier_oa.oa_pk = t.jw_oa_carrieraddress
LEFT JOIN OrgHeader  carrier    ON carrier.oh_pk = carrier_oa.oa_oh
WHERE t.jw_parentguid = @id
    AND t.jw_parenttype = 'CON'
ORDER BY t.jw_legorder;

-- 运单列表：发货人/收货人/承运人都在 JobDocAddress、OrgAddress 上，s.* 拿不到；
-- 件数取 js_outerpacks 而不是 js_totalpackagecount（后者实测基本恒为 0）
SELECT s.*,
    (SELECT TOP 1 oh.oh_fullname
     FROM JobDocAddress d
     INNER JOIN OrgAddress oa ON oa.oa_pk = d.e2_oa_address
     INNER JOIN OrgHeader  oh ON oh.oh_pk = oa.oa_oh
     WHERE d.e2_parentid = s.js_pk
        AND d.e2_parenttablecode = 'JS'
        AND d.e2_addresstype = 'CRD')            AS shipper_name,
    (SELECT TOP 1 oh.oh_fullname
     FROM JobDocAddress d
     INNER JOIN OrgAddress oa ON oa.oa_pk = d.e2_oa_address
     INNER JOIN OrgHeader  oh ON oh.oh_pk = oa.oa_oh
     WHERE d.e2_parentid = s.js_pk
        AND d.e2_parenttablecode = 'JS'
        AND d.e2_addresstype = 'CEG')            AS consignee_name,
    (SELECT TOP 1 oh.oh_fullname
     FROM OrgAddress oa
     INNER JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
     WHERE oa.oa_pk = s.js_oa_bookedshippinglineaddress) AS carrier_name,
    s.js_actualvolume     AS cbm,
    s.js_outerpacks       AS packages,
    s.js_f3_nkpacktype    AS pack_type,
    s.js_goodsdescription AS goods_description
FROM JobShipment s
INNER JOIN JobConShipLink l ON l.jn_js = s.js_pk
WHERE l.jn_jk = @id;

-- 集装箱：箱型要把 jc_rc 这个 pk 换成代码/描述；件数、体积、品名 JobContainer 上没有有效值，
-- 经中间表 JobContainerPackPivot 从 JobPackLines 汇总
SELECT t.*,
    -- RC_Code 是定长 char，不 RTRIM 会带出 '40HC      ' 这种尾随空格
    RTRIM(rc.RC_Code) AS container_type_code,
    rc.RC_Description AS container_type_desc,
    agg.pack_count,
    agg.pack_type,
    agg.total_volume,
    agg.total_weight,
    gd.goods_description
FROM JobContainer t
LEFT JOIN RefContainer rc ON rc.RC_PK = t.jc_rc
OUTER APPLY (
    SELECT SUM(jl.jl_packagecount)    AS pack_count,
           MAX(jl.jl_f3_nkpacktype)   AS pack_type,
           SUM(jl.jl_actualvolume)    AS total_volume,
           SUM(jl.jl_actualweight)    AS total_weight
    FROM JobContainerPackPivot p
    INNER JOIN JobPackLines jl ON jl.jl_pk = p.J6_JL
    WHERE p.J6_JC = t.jc_pk
) agg
-- jl_description 是 varchar(max)，聚合函数用不了，单独取第一条非空的
OUTER APPLY (
    SELECT TOP 1 jl2.jl_description AS goods_description
    FROM JobContainerPackPivot p2
    INNER JOIN JobPackLines jl2 ON jl2.jl_pk = p2.J6_JL
    WHERE p2.J6_JC = t.jc_pk
        AND jl2.jl_description <> ''
    ORDER BY jl2.jl_pk
) gd
WHERE t.jc_jk = @id
    AND t.jc_isvalid = 0
";

                using (var multi = await _appSqlServerRepository.QueryMultipleAsync(sql, dp))
                {
                    var details = (await multi.ReadAsync<ConsolidationDetailOutput>()).ToList();
                    var detail = details.FirstOrDefault();
                    if (detail == null) return null;

                    var agents = (await multi.ReadAsync<ConsolidationAgentOutput>()).ToList();
                    var orgAddresses = (await multi.ReadAsync<ConsolidationOrgAddressOutput>()).ToList();

                    detail.local_agent = agents.FirstOrDefault(a => a.e2_addresstype == "CEC");
                    detail.overseas_agent = agents.FirstOrDefault(a => a.e2_addresstype == "CIC");

                    if (detail.local_agent != null)
                        detail.local_agent.org_address = orgAddresses.FirstOrDefault(o => o.oa_pk == detail.local_agent.e2_oa_address);
                    if (detail.overseas_agent != null)
                        detail.overseas_agent.org_address = orgAddresses.FirstOrDefault(o => o.oa_pk == detail.overseas_agent.e2_oa_address);

                    FillAgentName(detail.local_agent);
                    FillAgentName(detail.overseas_agent);

                    if (!multi.IsConsumed)
                        detail.transport_list =
                            (await multi.ReadAsync<ConsolidationTransportOutput>()).ToList();

                    if (!multi.IsConsumed)
                        detail.shps = (await multi.ReadAsync<ConsolidationShipmentOutput>()).ToList();

                    if (!multi.IsConsumed)
                        detail.containers = (await multi.ReadAsync<ConsolidationContainerOutput>()).ToList();

                    FillShipmentCarrierFallback(detail);

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

        /// <summary>
        /// 把代理名称拍平到 name / code。
        /// JobDocAddress 上的 e2_companyname 在 CEC/CIC 行上实测几乎全空（13300 条里 13299 条为空），
        /// 所以以 OrgHeader.oh_fullname 为准，它没有时才回退到 e2_companyname。
        /// </summary>
        private static void FillAgentName(ConsolidationAgentOutput agent)
        {
            if (agent == null)
                return;

            agent.name = !string.IsNullOrWhiteSpace(agent.org_address?.oh_fullname)
                ? agent.org_address.oh_fullname
                : agent.e2_companyname;
            agent.code = agent.org_address?.oa_code;
        }

        /// <summary>
        /// 运单自身的承运人（js_oa_bookedshippinglineaddress）实测只有 47% 有值，
        /// 合单下的运单常常整片为空 —— 这种情况下承运人实际由合单的主程航段决定，
        /// 用它兜底，前端才不会整列空白。
        /// </summary>
        private static void FillShipmentCarrierFallback(ConsolidationDetailOutput detail)
        {
            if (detail.shps.Count == 0 || detail.transport_list.Count == 0)
                return;

            var mainLeg = detail.transport_list.FirstOrDefault(t =>
                              string.Equals(t.jw_transporttype, "MAI", StringComparison.OrdinalIgnoreCase))
                          ?? detail.transport_list.First();

            if (string.IsNullOrWhiteSpace(mainLeg.carrier_name))
                return;

            foreach (var shp in detail.shps)
            {
                if (string.IsNullOrWhiteSpace(shp.carrier_name))
                    shp.carrier_name = mainLeg.carrier_name;
            }
        }
    }
}
