using Dapper;
using SntBackend.Application.Consolidation.Dto;
using SntBackend.Application.Excel;
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
        private readonly CurrentCompanyNameProvider _currentCompanyNameProvider;

        public ConsolidationApplication(
            IAppSqlServerRepository appSqlServerRepository,
            CurrentCompanyNameProvider currentCompanyNameProvider)
        {
            _appSqlServerRepository = appSqlServerRepository;
            _currentCompanyNameProvider = currentCompanyNameProvider;
        }

        private static bool IsShipmentField(string key) => key.StartsWith("js_");

        private static string GetColumnExpr(string key)
        {
            if (IsShipmentField(key))
                return $"s.{key}";
            return $"t.{key}";
        }

        /// <summary>
        /// 使用现有的 jk_consolchargeable 字段承载展示兜底值，避免为查询结果
        /// 增加新的 DTO 字段。仅对主表值为 0 的合单按关联运单汇总。
        /// </summary>
        private async Task FillChargeableFallbackAsync(IEnumerable<JobConsolDtoOutput> items)
        {
            var itemList = items.ToList();
            var pending = itemList
                .Where(x => x.jk_consolchargeable == 0 && !string.IsNullOrWhiteSpace(x.jk_pk))
                .Select(x => x.jk_pk)
                .Distinct()
                .ToArray();
            if (pending.Length == 0)
                return;

            var rows = await _appSqlServerRepository.QueryAsync<ChargeableFallbackRow>(@"
SELECT l.jn_jk AS jk_pk, ISNULL(SUM(s.js_actualchargeable), 0) AS chargeable
FROM JobConShipLink l
INNER JOIN JobShipment s ON s.js_pk = l.jn_js
WHERE l.jn_jk IN @jkPks
GROUP BY l.jn_jk", new { jkPks = pending });

            var values = rows.ToDictionary(x => x.jk_pk, x => x.chargeable, StringComparer.OrdinalIgnoreCase);
            foreach (var item in itemList)
            {
                if (item.jk_consolchargeable == 0 && values.TryGetValue(item.jk_pk, out var chargeable))
                    item.jk_consolchargeable = chargeable;
            }
        }

        private sealed class ChargeableFallbackRow
        {
            public string jk_pk { get; set; }
            public decimal chargeable { get; set; }
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

                var key = item.key;
                var isLike = item.op is "Contain" or "Not Contain";
                var value = item.val.Trim();
                if (item.op != "between" && string.IsNullOrWhiteSpace(value))
                    continue;

                var paramName = $"@p{dp.ParameterNames.Count()}";
                var parameterValue = item.op switch
                {
                    _ when isLike => $"%{value}%",
                    _ => value
                };

                if (key is "company" or "company_pk" or "jh_gc")
                {
                    parts.Add($@" AND EXISTS (
    SELECT 1
    FROM JobHeader jh
    LEFT JOIN GlbCompany gc ON gc.GC_PK = jh.jh_gc
    WHERE jh.jh_parentid = t.jk_pk
      AND jh.jh_parenttablecode = 'JK'
      AND (jh.jh_gc {MapOp(item.op)} {paramName}
           OR gc.GC_Code {MapOp(item.op)} {paramName}
           OR gc.GC_Name {MapOp(item.op)} {paramName})
) ");
                    dp.Add(paramName, parameterValue);
                    continue;
                }

                if (key is "sales" or "sales_rep" or "jh_gs_nkrepsales")
                {
                    parts.Add($@" AND EXISTS (
    SELECT 1
    FROM JobHeader jh
    LEFT JOIN GlbStaff gs ON gs.GS_Code = jh.jh_gs_nkrepsales
    WHERE jh.jh_parentid = t.jk_pk
      AND jh.jh_parenttablecode = 'JK'
      AND (jh.jh_gs_nkrepsales {MapOp(item.op)} {paramName}
           OR gs.GS_Code {MapOp(item.op)} {paramName}
           OR gs.GS_FullName {MapOp(item.op)} {paramName})
) ");
                    dp.Add(paramName, parameterValue);
                    continue;
                }

                if (key is "operation" or "operator" or "jh_gs_nkrepops")
                {
                    parts.Add($@" AND EXISTS (
    SELECT 1
    FROM JobHeader jh
    LEFT JOIN GlbStaff gs ON gs.GS_Code = jh.jh_gs_nkrepops
    WHERE jh.jh_parentid = t.jk_pk
      AND jh.jh_parenttablecode = 'JK'
      AND (jh.jh_gs_nkrepops {MapOp(item.op)} {paramName}
           OR gs.GS_Code {MapOp(item.op)} {paramName}
           OR gs.GS_FullName {MapOp(item.op)} {paramName})
) ");
                    dp.Add(paramName, parameterValue);
                    continue;
                }

                if (key is "customer_service" or "customerService" or "shp_job_rep_cs_name")
                {
                    parts.Add($@" AND EXISTS (
    SELECT 1
    FROM GenCustomAddOnValue v
    WHERE v.Xv_ParentID = t.jk_pk
      AND v.XV_Name IN ('Customer Service', 'Coustomer Service')
      AND v.XV_Data {MapOp(item.op)} {paramName}
) ");
                    dp.Add(paramName, parameterValue);
                    continue;
                }

                if (IsShipmentField(key))
                    joinShipment = true;

                var col = GetColumnExpr(key);

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
                    parts.Add($" AND {col} {MapOp(item.op)} {paramName}");
                    dp.Add(paramName, parameterValue);
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
SELECT {distinct} t.*,
    transport.con_etd,
    transport.con_eta,
    transport.con_vessel,
    transport.con_voyage,
    transport.con_carrier_name,
    containers.ctr_type,
    containers.ctr_count,
    jobHeader.jh_gc,
    jobHeader.company_name,
    jobHeader.jh_gs_nkrepsales,
    jobHeader.sales_name,
    jobHeader.jh_gs_nkrepops,
    jobHeader.operation_name,
    customerService.customer_service,
    customerService.customer_service_name
FROM JobConsol t
{shipmentJoin}
OUTER APPLY (
    SELECT TOP 1
        ct.jw_etd AS con_etd,
        ct.jw_eta AS con_eta,
        ct.jw_vessel AS con_vessel,
        ct.jw_voyageflight AS con_voyage,
        carrier.oh_fullname AS con_carrier_name
    FROM JobConsolTransport ct
    LEFT JOIN OrgAddress carrier_oa ON carrier_oa.oa_pk = ct.jw_oa_carrieraddress
    LEFT JOIN OrgHeader carrier ON carrier.oh_pk = carrier_oa.oa_oh
    WHERE ct.jw_parentguid = t.jk_pk
      AND ct.jw_parenttype = 'CON'
      AND ct.jw_isvalid = 1
    ORDER BY CASE WHEN ct.jw_transporttype = 'MAI' THEN 0 ELSE 1 END,
             ct.jw_legorder,
             ct.jw_pk
) transport
OUTER APPLY (
    SELECT
        STUFF((
            SELECT DISTINCT ',' + NULLIF(RTRIM(rc.RC_Code), '')
            FROM JobContainer jc
            LEFT JOIN RefContainer rc ON rc.RC_PK = jc.jc_rc
            WHERE jc.jc_jk = t.jk_pk
              AND jc.jc_isvalid = 0
              AND NULLIF(RTRIM(rc.RC_Code), '') IS NOT NULL
            FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ctr_type,
        (SELECT SUM(ISNULL(jc.jc_containercount, 1))
         FROM JobContainer jc
         WHERE jc.jc_jk = t.jk_pk
           AND jc.jc_isvalid = 0) AS ctr_count
) containers
OUTER APPLY (
    SELECT TOP 1
        jh.jh_gc,
        gc.GC_Name AS company_name,
        jh.jh_gs_nkrepsales,
        salesStaff.GS_FullName AS sales_name,
        jh.jh_gs_nkrepops,
        operationStaff.GS_FullName AS operation_name
    FROM JobHeader jh
    LEFT JOIN GlbCompany gc ON gc.GC_PK = jh.jh_gc
    LEFT JOIN GlbStaff salesStaff ON salesStaff.GS_Code = jh.jh_gs_nkrepsales
    LEFT JOIN GlbStaff operationStaff ON operationStaff.GS_Code = jh.jh_gs_nkrepops
    WHERE CONVERT(VARCHAR(50), jh.jh_parentid) = CONVERT(VARCHAR(50), t.jk_pk)
      AND jh.jh_parenttablecode = 'JK'
    ORDER BY jh.jh_pk
) jobHeader
OUTER APPLY (
    SELECT TOP 1
        v.XV_Data AS customer_service,
        COALESCE(gs.GS_FullName, v.XV_Data) AS customer_service_name
    FROM GenCustomAddOnValue v
    LEFT JOIN GlbStaff gs ON gs.GS_Code = v.XV_Data
    WHERE v.Xv_ParentID = t.jk_pk
      AND v.XV_Name IN ('Customer Service', 'Coustomer Service')
      AND NULLIF(v.XV_Data, '') IS NOT NULL
    ORDER BY v.XV_PK
) customerService
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
                var list = (await multi.ReadAsync<ConsolidationTblRowOutput>()).ToList();
                await FillChargeableFallbackAsync(list);

                output.TotalCount = total;
                output.Items = list;
            }

            return output;
        }

        public async Task<byte[]> Export(ConsolidationTblInput input)
        {
            input ??= new ConsolidationTblInput();
            var dp = new DynamicParameters();
            var whereIf = TblBuildWhere(input.filters ?? new List<ConsolidationTblFilterItem>(), dp, out var joinShipment);
            if (input.selectedPks?.Count > 0)
            {
                whereIf += " AND t.jk_pk IN @selectedPks ";
                dp.Add("selectedPks", input.selectedPks);
            }
            var shipmentJoin = joinShipment
                ? "LEFT JOIN JobConShipLink l ON l.jn_jk = t.jk_pk LEFT JOIN JobShipment s ON s.js_pk = l.jn_js"
                : "";
            var distinct = joinShipment ? "DISTINCT" : "";
            var sql = @$"
SELECT {distinct}
    t.jk_pk,
    t.jk_transportmode,
    t.jk_consolmode,
    t.jk_uniqueconsignref,
    t.jk_masterbillnum,
    t.jk_rl_nkloadport,
    t.jk_rl_nkdischargeport,
    t.jk_shippedonboarddate,
    t.jk_systemcreateuser,
    t.jk_systemlastedituser,
    t.jk_phase,
    t.jk_consolchargeable,
    t.jk_iscancelled,
    transport.con_etd,
    transport.con_eta,
    transport.con_vessel,
    transport.con_voyage,
    transport.con_carrier_name,
    containers.ctr_type,
    containers.ctr_count,
    jobHeader.company_name,
    jobHeader.sales_name,
    jobHeader.operation_name,
    customerService.customer_service_name
FROM JobConsol t
{shipmentJoin}
OUTER APPLY (
    SELECT TOP 1
        ct.jw_etd AS con_etd,
        ct.jw_eta AS con_eta,
        ct.jw_vessel AS con_vessel,
        ct.jw_voyageflight AS con_voyage,
        carrier.oh_fullname AS con_carrier_name
    FROM JobConsolTransport ct
    LEFT JOIN OrgAddress carrier_oa ON carrier_oa.oa_pk = ct.jw_oa_carrieraddress
    LEFT JOIN OrgHeader carrier ON carrier.oh_pk = carrier_oa.oa_oh
    WHERE ct.jw_parentguid = t.jk_pk
      AND ct.jw_parenttype = 'CON'
      AND ct.jw_isvalid = 1
    ORDER BY CASE WHEN ct.jw_transporttype = 'MAI' THEN 0 ELSE 1 END,
             ct.jw_legorder,
             ct.jw_pk
) transport
OUTER APPLY (
    SELECT
        STUFF((
            SELECT DISTINCT ',' + NULLIF(RTRIM(rc.RC_Code), '')
            FROM JobContainer jc
            LEFT JOIN RefContainer rc ON rc.RC_PK = jc.jc_rc
            WHERE jc.jc_jk = t.jk_pk
              AND jc.jc_isvalid = 0
              AND NULLIF(RTRIM(rc.RC_Code), '') IS NOT NULL
            FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ctr_type,
        (SELECT SUM(ISNULL(jc.jc_containercount, 1))
         FROM JobContainer jc
         WHERE jc.jc_jk = t.jk_pk
           AND jc.jc_isvalid = 0) AS ctr_count
) containers
OUTER APPLY (
    SELECT TOP 1
        gc.GC_Name AS company_name,
        salesStaff.GS_FullName AS sales_name,
        operationStaff.GS_FullName AS operation_name
    FROM JobHeader jh
    LEFT JOIN GlbCompany gc ON gc.GC_PK = jh.jh_gc
    LEFT JOIN GlbStaff salesStaff ON salesStaff.GS_Code = jh.jh_gs_nkrepsales
    LEFT JOIN GlbStaff operationStaff ON operationStaff.GS_Code = jh.jh_gs_nkrepops
    WHERE CONVERT(VARCHAR(50), jh.jh_parentid) = CONVERT(VARCHAR(50), t.jk_pk)
      AND jh.jh_parenttablecode = 'JK'
    ORDER BY jh.jh_pk
) jobHeader
OUTER APPLY (
    SELECT TOP 1 COALESCE(gs.GS_FullName, v.XV_Data) AS customer_service_name
    FROM GenCustomAddOnValue v
    LEFT JOIN GlbStaff gs ON gs.GS_Code = v.XV_Data
    WHERE v.Xv_ParentID = t.jk_pk
      AND v.XV_Name IN ('Customer Service', 'Coustomer Service')
      AND NULLIF(v.XV_Data, '') IS NOT NULL
    ORDER BY v.XV_PK
) customerService
WHERE t.jk_iscancelled = 0
    {whereIf}
ORDER BY t.jk_pk DESC
OPTION (RECOMPILE)
";
            var items = (await _appSqlServerRepository.QueryAsync<ConsolidationTblRowOutput>(sql, dp)).ToList();
            await FillChargeableFallbackAsync(items);
            var defaults = new List<ExportColumnInput>
            {
                new() { key = "jk_transportmode", title = "Trans Mode" },
                new() { key = "jk_consolmode", title = "Container Mode" },
                new() { key = "jk_uniqueconsignref", title = "Consol No." },
                new() { key = "jk_masterbillnum", title = "Master No." },
                new() { key = "jk_rl_nkloadport", title = "Load Port" },
                new() { key = "jk_rl_nkdischargeport", title = "Discharge Port" },
                new() { key = "con_etd", title = "ETD" },
                new() { key = "con_eta", title = "ETA" },
                new() { key = "con_vessel", title = "Vessel" },
                new() { key = "con_voyage", title = "Voyage" },
                new() { key = "con_carrier_name", title = "Carrier" },
                new() { key = "ctr_type", title = "Ctr Type" },
                new() { key = "ctr_count", title = "Ctr Count" },
                new() { key = "jk_shippedonboarddate", title = "On Board Date" },
                new() { key = "jk_systemcreateuser", title = "Create User" },
                new() { key = "jk_systemlastedituser", title = "Modify User" },
                new() { key = "company_name", title = "Company" },
                new() { key = "sales_name", title = "Sales" },
                new() { key = "operation_name", title = "Operation" },
                new() { key = "customer_service_name", title = "Customer Service" },
                new() { key = "jk_phase", title = "Phase" },
                new() { key = "jk_consolchargeable", title = "Consol Chargeable" },
                new() { key = "jk_iscancelled", title = "Cancelled" }
            };
            var columns = SjcExcelExportHelper.ResolveColumns(input.columns, defaults);
            var values = new Dictionary<string, Func<ConsolidationTblRowOutput, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["jk_transportmode"] = item => item.jk_transportmode,
                ["jk_consolmode"] = item => item.jk_consolmode,
                ["jk_uniqueconsignref"] = item => item.jk_uniqueconsignref,
                ["jk_masterbillnum"] = item => item.jk_masterbillnum,
                ["jk_rl_nkloadport"] = item => item.jk_rl_nkloadport,
                ["jk_rl_nkdischargeport"] = item => item.jk_rl_nkdischargeport,
                ["con_etd"] = item => item.con_etd,
                ["con_eta"] = item => item.con_eta,
                ["con_vessel"] = item => item.con_vessel,
                ["con_voyage"] = item => item.con_voyage,
                ["con_carrier_name"] = item => item.con_carrier_name,
                ["ctr_type"] = item => item.ctr_type,
                ["ctr_count"] = item => item.ctr_count,
                ["jk_shippedonboarddate"] = item => item.jk_shippedonboarddate,
                ["jk_systemcreateuser"] = item => item.jk_systemcreateuser,
                ["jk_systemlastedituser"] = item => item.jk_systemlastedituser,
                ["jh_gc"] = item => item.company_name,
                ["jh_gs_nkrepsales"] = item => item.sales_name,
                ["jh_gs_nkrepops"] = item => item.operation_name,
                ["customer_service"] = item => item.customer_service_name,
                ["jk_phase"] = item => item.jk_phase,
                ["company_name"] = item => item.company_name,
                ["sales_name"] = item => item.sales_name,
                ["operation_name"] = item => item.operation_name,
                ["customer_service_name"] = item => item.customer_service_name,
                ["jk_consolchargeable"] = item => item.jk_consolchargeable,
                ["jk_iscancelled"] = item => item.jk_iscancelled == 1 ? "Y" : "N"
            };
            var headers = columns.Select(item => item.title).ToList();
            var rows = items.Select(item => (IReadOnlyList<object>)columns
                .Select(column => values.TryGetValue(column.key, out var getter) ? getter(item) : null)
                .ToList()).ToList();
            var numericColumns = new HashSet<int>(columns
                .Select((column, index) => (column, index))
                .Where(pair => pair.column.key is "ctr_count" or "jk_consolchargeable")
                .Select(pair => pair.index));
            return SjcExcelExportHelper.Build(
                "Consols",
                string.IsNullOrWhiteSpace(input.exportTitle) ? "Consols" : input.exportTitle,
                await _currentCompanyNameProvider.GetAsync(),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                headers,
                rows,
                numericColumns,
                companyNameLabel: input.companyNameLabel,
                reportPeriodLabel: input.reportPeriodLabel);
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

                    // 多结果集全部读取完毕后再查询汇总，避免同一连接上出现并行 DataReader。
                    await FillChargeableFallbackAsync(new List<JobConsolDtoOutput> { detail });
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
