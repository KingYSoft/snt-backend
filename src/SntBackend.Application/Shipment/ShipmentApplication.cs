using Dapper;
using SntBackend.Application.Po.Dto;
using SntBackend.Application.Shipment.Dto;
using SntBackend.Application.Excel;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Shipment
{
    public class ShipmentApplication : SntBackendApplicationBase, IShipmentApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        private readonly CurrentCompanyNameProvider _currentCompanyNameProvider;

        public ShipmentApplication(
            IAppSqlServerRepository appSqlServerRepository,
            CurrentCompanyNameProvider currentCompanyNameProvider)
        {
            _appSqlServerRepository = appSqlServerRepository;
            _currentCompanyNameProvider = currentCompanyNameProvider;
        }

        private static string TblBuildWhere(List<ShipmentTblFilterItem> filters, DynamicParameters dp)
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

                if (string.Equals(item.key, "js_consolreference", StringComparison.OrdinalIgnoreCase))
                {
                    if (item.op == "between")
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.val))
                    {
                        continue;
                    }

                    var consolReference = $@"COALESCE(NULLIF(LTRIM(RTRIM(t.js_consolreference)), ''),
    (SELECT TOP 1 NULLIF(LTRIM(RTRIM(c.jk_uniqueconsignref)), '')
     FROM JobConShipLink l
     INNER JOIN JobConsol c ON c.jk_pk = l.jn_jk
     WHERE l.jn_js = t.js_pk
       AND c.jk_iscancelled = 0
     ORDER BY c.jk_pk))";
                    var consolParam = $"@p{dp.ParameterNames.Count()}";
                    var isContain = item.op == "Contain" || item.op == "Not Contain";
                    parts.Add($" AND {consolReference} {MapOp(item.op)} {consolParam} ");
                    dp.Add(consolParam, isContain ? $"%{item.val.Trim()}%" : item.val.Trim());
                    continue;
                }

                if (item.op == "between")
                {
                    if (!string.IsNullOrWhiteSpace(item.start) && DateTime.TryParse(item.start, out var startDate))
                    {
                        var paramNameStart = $"@p{dp.ParameterNames.Count()}";
                        parts.Add($" AND t.{item.key} >= {paramNameStart} ");
                        dp.Add(paramNameStart, startDate);
                    }
                    if (!string.IsNullOrWhiteSpace(item.end) && DateTime.TryParse(item.end, out var endDate))
                    {
                        var paramNameEnd = $"@p{dp.ParameterNames.Count()}";
                        parts.Add($" AND t.{item.key} < {paramNameEnd}");
                        dp.Add(paramNameEnd, endDate.AddDays(1));
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

        public Task<ShipmentTblOutput> Tbl(ShipmentTblInput input)
        {
            return TblInternal(input ?? new ShipmentTblInput(), true);
        }

        private async Task<ShipmentTblOutput> TblInternal(ShipmentTblInput input, bool includeTotalCount)
        {
            var output = new ShipmentTblOutput();
            var dp = new DynamicParameters();
            var whereIf = TblBuildWhere(input.filters, dp);

            var totalSql = @$"
SELECT COUNT(*)
FROM JobShipment t
WHERE 1 = 1
    AND t.js_iscancelled = 0 
    {whereIf}
";
            var pageSql = @$"
WITH PageShipments AS
(
    SELECT t.*
    FROM JobShipment t
    WHERE 1 = 1
        AND t.js_iscancelled = 0
        {whereIf}
    ORDER BY t.js_pk DESC
    OFFSET @skipCount ROWS FETCH NEXT @takeCount ROWS ONLY
), CandidateContainers AS
(
    SELECT p.js_pk, jc.jc_pk
    FROM PageShipments p
    INNER JOIN JobContainer jc
        ON jc.jc_js_fclbookingonlylink = p.js_pk
       AND jc.jc_isvalid = 0

    UNION

    SELECT p.js_pk, piv.J6_JC
    FROM PageShipments p
    INNER JOIN JobPackLines jl ON jl.jl_js = p.js_pk
    INNER JOIN JobContainerPackPivot piv ON piv.J6_JL = jl.jl_pk

    UNION

    SELECT p.js_pk, jc.jc_pk
    FROM PageShipments p
    INNER JOIN JobConShipLink link ON link.jn_js = p.js_pk
    INNER JOIN JobContainer jc
        ON jc.jc_jk = link.jn_jk
       AND jc.jc_isvalid = 0
), ContainerAggregate AS
(
    SELECT cc.js_pk,
        MAX(RTRIM(rc.RC_Code)) AS pac_container_type,
        SUM(ISNULL(jc.jc_containercount, 1)) AS pac_container_count,
        STRING_AGG(CONVERT(VARCHAR(MAX), NULLIF(RTRIM(jc.jc_containernum), '')), ',') AS pac_container_num
    FROM CandidateContainers cc
    INNER JOIN JobContainer jc
        ON jc.jc_pk = cc.jc_pk
       AND jc.jc_isvalid = 0
    LEFT JOIN RefContainer rc ON rc.RC_PK = jc.jc_rc
    GROUP BY cc.js_pk
)
SELECT t.*,
    consol.con_master_bill_num,
    consol.linked_consol_reference,
    consol.con_vessel,
    consol.con_voyage,
    shipper.shipper_name,
    consignee.consignee_name,
    cargo.shp_cargo_ready,
    t.js_hblawbchargesdisplay AS shp_freight_terms,
    t.js_hblcontainerpackmodeoverride AS shp_delivery_mode,
    carrier.carrier_name,
    containers.pac_container_type,
    containers.pac_container_count,
    containers.pac_container_num,
    sales.shp_job_rep_sales_name
FROM PageShipments t
OUTER APPLY (
    SELECT TOP 1
        c.jk_masterbillnum AS con_master_bill_num,
        c.jk_uniqueconsignref AS linked_consol_reference,
        ct.jw_vessel AS con_vessel,
        ct.jw_voyageflight AS con_voyage
    FROM JobConShipLink l
    INNER JOIN JobConsol c ON c.jk_pk = l.jn_jk
    LEFT JOIN JobConsolTransport ct
        ON ct.jw_parentguid = c.jk_pk
       AND ct.jw_parenttype = 'CON'
       AND ct.jw_isvalid = 1
    WHERE l.jn_js = t.js_pk
      AND c.jk_iscancelled = 0
    ORDER BY CASE WHEN ct.jw_transporttype = 'MAI' THEN 0 ELSE 1 END,
             ct.jw_legorder,
             c.jk_pk
) consol
OUTER APPLY (
    SELECT TOP 1
        COALESCE(oh.oh_fullname, d.e2_companyname) AS shipper_name
    FROM JobDocAddress d
    LEFT JOIN OrgAddress oa ON oa.oa_pk = d.e2_oa_address
    LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
    WHERE d.e2_parentid = t.js_pk
      AND d.e2_parenttablecode = 'JS'
      AND d.e2_addresstype = 'CRD'
    ORDER BY d.e2_pk
) shipper
OUTER APPLY (
    SELECT TOP 1
        COALESCE(oh.oh_fullname, d.e2_companyname) AS consignee_name
    FROM JobDocAddress d
    LEFT JOIN OrgAddress oa ON oa.oa_pk = d.e2_oa_address
    LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
    WHERE d.e2_parentid = t.js_pk
      AND d.e2_parenttablecode = 'JS'
      AND d.e2_addresstype = 'CEG'
    ORDER BY d.e2_pk
) consignee
OUTER APPLY (
    SELECT TOP 1 v.XV_Data AS shp_cargo_ready
    FROM GenCustomAddOnValue v
    WHERE v.Xv_ParentID = t.js_pk
      AND v.XV_Name = 'Cargo Ready Date'
      AND NULLIF(v.XV_Data, '') IS NOT NULL
) cargo
OUTER APPLY (
    SELECT TOP 1 oh.oh_fullname AS carrier_name
    FROM OrgAddress oa
    LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
    WHERE oa.oa_pk = t.js_oa_bookedshippinglineaddress
) carrier
LEFT JOIN ContainerAggregate containers ON containers.js_pk = t.js_pk
OUTER APPLY (
    SELECT TOP 1 gs.GS_FullName AS shp_job_rep_sales_name
    FROM JobHeader jh
    LEFT JOIN GlbStaff gs
        ON gs.GS_Code = jh.jh_gs_nkrepsales
    WHERE CONVERT(VARCHAR(50), jh.jh_parentid) = CONVERT(VARCHAR(50), t.js_pk)
      AND jh.jh_parenttablecode = 'JS'
    ORDER BY jh.jh_pk
) sales
";
            dp.Add("skipCount", input.SkipCount);
            dp.Add("takeCount", input.MaxResultCount);

            var query = includeTotalCount ? $@"
{totalSql};
{pageSql}
" : pageSql;
            using (var multi = await _appSqlServerRepository.QueryMultipleAsync(query, dp))
            {
                var total = includeTotalCount ? await multi.ReadFirstAsync<int>() : 0;
                var list = (await multi.ReadAsync<ShipmentTblRowOutput>()).ToList();
                foreach (var item in list)
                {
                    if (string.IsNullOrWhiteSpace(item.js_consolreference))
                    {
                        item.js_consolreference = item.linked_consol_reference;
                    }
                }

                output.TotalCount = total;
                output.Items = list;
            }

            return output;
        }

        public async Task<byte[]> Export(ShipmentTblInput input)
        {
            input ??= new ShipmentTblInput();
            var dp = new DynamicParameters();
            var whereIf = TblBuildWhere(input.filters ?? new List<ShipmentTblFilterItem>(), dp);
            if (input.selectedPks?.Count > 0)
            {
                whereIf += " AND t.js_pk IN @selectedPks ";
                dp.Add("selectedPks", input.selectedPks);
            }
            var sql = @$"
WITH FilteredShipments AS
(
    SELECT
        t.js_pk,
        t.js_transportmode,
        t.js_packingmode,
        t.js_shipmentstatus,
        t.js_uniqueconsignref,
        t.js_consolreference,
        t.js_housebill,
        t.js_rl_nkorigin,
        t.js_rl_nkdestination,
        t.js_e_dep,
        t.js_e_arv,
        t.js_shippedonboarddate,
        t.js_actualweight,
        t.js_actualvolume,
        t.js_actualchargeable,
        t.js_outerpacks,
        t.js_f3_nkpacktype,
        t.js_goodsdescription,
        t.js_unitofweight,
        t.js_unitofvolume,
        t.js_hblawbchargesdisplay,
        t.js_inco,
        t.js_hblcontainerpackmodeoverride,
        t.js_releasetype,
        t.js_oa_bookedshippinglineaddress,
        t.js_systemcreateuser,
        t.js_systemlastedituser
    FROM JobShipment t
    WHERE t.js_iscancelled = 0
        {whereIf}
), CandidateContainers AS
(
    SELECT p.js_pk, jc.jc_pk
    FROM FilteredShipments p
    INNER JOIN JobContainer jc
        ON jc.jc_js_fclbookingonlylink = p.js_pk
       AND jc.jc_isvalid = 0

    UNION

    SELECT p.js_pk, piv.J6_JC
    FROM FilteredShipments p
    INNER JOIN JobPackLines jl ON jl.jl_js = p.js_pk
    INNER JOIN JobContainerPackPivot piv ON piv.J6_JL = jl.jl_pk

    UNION

    SELECT p.js_pk, jc.jc_pk
    FROM FilteredShipments p
    INNER JOIN JobConShipLink link ON link.jn_js = p.js_pk
    INNER JOIN JobContainer jc
        ON jc.jc_jk = link.jn_jk
       AND jc.jc_isvalid = 0
), ContainerAggregate AS
(
    SELECT cc.js_pk,
        MAX(RTRIM(rc.RC_Code)) AS pac_container_type,
        SUM(ISNULL(jc.jc_containercount, 1)) AS pac_container_count,
        STRING_AGG(CONVERT(VARCHAR(MAX), NULLIF(RTRIM(jc.jc_containernum), '')), ',') AS pac_container_num
    FROM CandidateContainers cc
    INNER JOIN JobContainer jc
        ON jc.jc_pk = cc.jc_pk
       AND jc.jc_isvalid = 0
    LEFT JOIN RefContainer rc ON rc.RC_PK = jc.jc_rc
    GROUP BY cc.js_pk
)
SELECT t.*,
    consol.con_master_bill_num,
    consol.linked_consol_reference,
    consol.con_vessel,
    consol.con_voyage,
    shipper.shipper_name,
    consignee.consignee_name,
    cargo.shp_cargo_ready,
    t.js_hblawbchargesdisplay AS shp_freight_terms,
    t.js_hblcontainerpackmodeoverride AS shp_delivery_mode,
    carrier.carrier_name,
    containers.pac_container_type,
    containers.pac_container_count,
    containers.pac_container_num,
    sales.shp_job_rep_sales_name
FROM FilteredShipments t
OUTER APPLY (
    SELECT TOP 1
        c.jk_masterbillnum AS con_master_bill_num,
        c.jk_uniqueconsignref AS linked_consol_reference,
        ct.jw_vessel AS con_vessel,
        ct.jw_voyageflight AS con_voyage
    FROM JobConShipLink l
    INNER JOIN JobConsol c ON c.jk_pk = l.jn_jk
    LEFT JOIN JobConsolTransport ct
        ON ct.jw_parentguid = c.jk_pk
       AND ct.jw_parenttype = 'CON'
       AND ct.jw_isvalid = 1
    WHERE l.jn_js = t.js_pk
      AND c.jk_iscancelled = 0
    ORDER BY CASE WHEN ct.jw_transporttype = 'MAI' THEN 0 ELSE 1 END,
             ct.jw_legorder,
             c.jk_pk
) consol
OUTER APPLY (
    SELECT TOP 1
        COALESCE(oh.oh_fullname, d.e2_companyname) AS shipper_name
    FROM JobDocAddress d
    LEFT JOIN OrgAddress oa ON oa.oa_pk = d.e2_oa_address
    LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
    WHERE d.e2_parentid = t.js_pk
      AND d.e2_parenttablecode = 'JS'
      AND d.e2_addresstype = 'CRD'
    ORDER BY d.e2_pk
) shipper
OUTER APPLY (
    SELECT TOP 1
        COALESCE(oh.oh_fullname, d.e2_companyname) AS consignee_name
    FROM JobDocAddress d
    LEFT JOIN OrgAddress oa ON oa.oa_pk = d.e2_oa_address
    LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
    WHERE d.e2_parentid = t.js_pk
      AND d.e2_parenttablecode = 'JS'
      AND d.e2_addresstype = 'CEG'
    ORDER BY d.e2_pk
) consignee
OUTER APPLY (
    SELECT TOP 1 v.XV_Data AS shp_cargo_ready
    FROM GenCustomAddOnValue v
    WHERE v.Xv_ParentID = t.js_pk
      AND v.XV_Name = 'Cargo Ready Date'
      AND NULLIF(v.XV_Data, '') IS NOT NULL
) cargo
OUTER APPLY (
    SELECT TOP 1 oh.oh_fullname AS carrier_name
    FROM OrgAddress oa
    LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
    WHERE oa.oa_pk = t.js_oa_bookedshippinglineaddress
) carrier
LEFT JOIN ContainerAggregate containers ON containers.js_pk = t.js_pk
OUTER APPLY (
    SELECT TOP 1 gs.GS_FullName AS shp_job_rep_sales_name
    FROM JobHeader jh
    LEFT JOIN GlbStaff gs
        ON gs.GS_Code = jh.jh_gs_nkrepsales
    WHERE CONVERT(VARCHAR(50), jh.jh_parentid) = CONVERT(VARCHAR(50), t.js_pk)
      AND jh.jh_parenttablecode = 'JS'
    ORDER BY jh.jh_pk
) sales
ORDER BY t.js_pk DESC
OPTION (RECOMPILE)
";
            var items = (await _appSqlServerRepository.QueryAsync<ShipmentTblRowOutput>(sql, dp)).ToList();
            var defaults = new List<ExportColumnInput>
            {
                new() { key = "js_transportmode", title = "Trans Mode" },
                new() { key = "js_packingmode", title = "Container Mode" },
                new() { key = "js_shipmentstatus", title = "Status" },
                new() { key = "js_uniqueconsignref", title = "Shipment No." },
                new() { key = "js_consolreference", title = "Consol No." },
                new() { key = "js_housebill", title = "House Bill" },
                new() { key = "con_master_bill_num", title = "Master Bill" },
                new() { key = "shipper_name", title = "Shipper Name" },
                new() { key = "consignee_name", title = "Consignee Name" },
                new() { key = "js_rl_nkorigin", title = "Origin" },
                new() { key = "js_rl_nkdestination", title = "Destination" },
                new() { key = "shp_cargo_ready", title = "CRD" },
                new() { key = "js_e_dep", title = "ETD" },
                new() { key = "js_e_arv", title = "ETA" },
                new() { key = "js_shippedonboarddate", title = "On Board Date" },
                new() { key = "js_actualweight", title = "Gross Weight" },
                new() { key = "js_actualvolume", title = "CBM" },
                new() { key = "js_actualchargeable", title = "Chargeable Weight" },
                new() { key = "js_outerpacks", title = "Package" },
                new() { key = "js_f3_nkpacktype", title = "Unit" },
                new() { key = "shp_freight_terms", title = "Freight Term" },
                new() { key = "js_inco", title = "Inco" },
                new() { key = "shp_delivery_mode", title = "Delivery Mode" },
                new() { key = "js_releasetype", title = "Release Type" },
                new() { key = "pac_container_type", title = "Ctr Type" },
                new() { key = "pac_container_count", title = "Ctr Count" },
                new() { key = "pac_container_num", title = "Ctr No." },
                new() { key = "carrier_name", title = "Carrier" },
                new() { key = "con_vessel", title = "Vessel" },
                new() { key = "con_voyage", title = "Voyage" },
                new() { key = "js_systemcreateuser", title = "Create User" },
                new() { key = "js_systemlastedituser", title = "Modify User" },
                new() { key = "shp_job_rep_sales_name", title = "Sales Rep" },
                new() { key = "js_goodsdescription", title = "Goods Description" }
            };
            var columns = SjcExcelExportHelper.ResolveColumns(input.columns, defaults);
            var values = new Dictionary<string, Func<ShipmentTblRowOutput, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["js_transportmode"] = item => item.js_transportmode,
                ["js_packingmode"] = item => item.js_packingmode,
                ["js_shipmentstatus"] = item => item.js_shipmentstatus,
                ["js_uniqueconsignref"] = item => item.js_uniqueconsignref,
                ["js_consolreference"] = item => string.IsNullOrWhiteSpace(item.js_consolreference)
                    ? item.linked_consol_reference
                    : item.js_consolreference,
                ["js_housebill"] = item => item.js_housebill,
                ["con_master_bill_num"] = item => item.con_master_bill_num,
                ["shipper_name"] = item => item.shipper_name,
                ["consignee_name"] = item => item.consignee_name,
                ["js_rl_nkorigin"] = item => item.js_rl_nkorigin,
                ["js_rl_nkdestination"] = item => item.js_rl_nkdestination,
                ["shp_cargo_ready"] = item => item.shp_cargo_ready,
                ["js_e_dep"] = item => item.js_e_dep,
                ["js_e_arv"] = item => item.js_e_arv,
                ["js_shippedonboarddate"] = item => item.js_shippedonboarddate,
                ["js_actualweight"] = item => item.js_actualweight == 0 ? "-" : $"{item.js_actualweight} {item.js_unitofweight ?? "KG"}",
                ["js_actualvolume"] = item => item.js_actualvolume == 0 ? "-" : $"{item.js_actualvolume} {item.js_unitofvolume ?? "M3"}",
                ["js_actualchargeable"] = item => item.js_actualchargeable,
                ["js_outerpacks"] = item => item.js_outerpacks,
                ["js_f3_nkpacktype"] = item => item.js_f3_nkpacktype,
                ["shp_freight_terms"] = item => item.shp_freight_terms,
                ["js_inco"] = item => item.js_inco,
                ["shp_delivery_mode"] = item => item.shp_delivery_mode,
                ["js_releasetype"] = item => item.js_releasetype,
                ["pac_container_type"] = item => item.pac_container_type,
                ["pac_container_count"] = item => item.pac_container_count,
                ["pac_container_num"] = item => item.pac_container_num,
                ["carrier_name"] = item => item.carrier_name,
                ["con_vessel"] = item => item.con_vessel,
                ["con_voyage"] = item => item.con_voyage,
                ["js_systemcreateuser"] = item => item.js_systemcreateuser,
                ["js_systemlastedituser"] = item => item.js_systemlastedituser,
                ["shp_job_rep_sales_name"] = item => item.shp_job_rep_sales_name,
                ["js_goodsdescription"] = item => item.js_goodsdescription
            };
            var headers = columns.Select(item => item.title).ToList();
            var rows = items.Select(item => (IReadOnlyList<object>)columns
                .Select(column => values.TryGetValue(column.key, out var getter) ? getter(item) : null)
                .ToList()).ToList();
            var numericColumns = new HashSet<int>(columns
                .Select((column, index) => (column, index))
                .Where(pair => pair.column.key is "js_actualchargeable" or "pac_container_count")
                .Select(pair => pair.index));
            return SjcExcelExportHelper.Build(
                "Shipments",
                string.IsNullOrWhiteSpace(input.exportTitle) ? "Shipments" : input.exportTitle,
                await _currentCompanyNameProvider.GetAsync(),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                headers,
                rows,
                numericColumns,
                companyNameLabel: input.companyNameLabel,
                reportPeriodLabel: input.reportPeriodLabel);
        }

        public async Task<ShipmentDetailOutput> Detail(string id)
        {
            var dp = new DynamicParameters();
            dp.Add("id", id, DbType.AnsiString);

            var sql = @"
SELECT t.*, 
    oh.oh_fullname as carrier_name,
    (SELECT oh_fullname FROM OrgHeader WHERE OH_PK = t.js_oh_handledonbehalfofforwarder) as booking_party_name
FROM JobShipment t
LEFT JOIN OrgAddress oa ON oa.oa_pk = t.js_oa_bookedshippinglineaddress
LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
WHERE t.js_pk = @id;

SELECT 
    t.e2_pk, t.e2_isvalid, t.e2_parentid, t.e2_parenttablecode, t.e2_addresstype, t.e2_oa_address,
    t.e2_companyname, t.e2_address1, t.e2_address2, t.e2_city, t.e2_state,
    t.e2_postcode, t.e2_rn_nkcountrycode, t.e2_phone, t.e2_fax, t.e2_email,
    t.e2_systemcreatetimeutc, t.e2_systemcreateuser, t.e2_systemlastedittimeutc, t.e2_systemlastedituser,
    CAST(t.e2_geolocation AS VARCHAR(MAX)) as e2_geolocation,
    t.e2_additionaladdressinformation
FROM JobDocAddress t
WHERE t.e2_parentid = @id
    AND t.e2_parenttablecode = 'JS';

SELECT 
    oa.oa_pk, oa.oa_isvalid, oa.oa_isactive, oa.oa_code,
    oa.oa_companynameoverride, oa.oa_address1, oa.oa_address2, 
    oa.oa_city, oa.oa_state, oa.oa_postcode, oa.oa_rn_nkcountrycode,
    oa.oa_phone, oa.oa_fax, oa.oa_mobile, oa.oa_email,
    oh.oh_fullname as oh_fullname
FROM JobDocAddress jda
INNER JOIN OrgAddress oa ON oa.oa_pk = jda.e2_oa_address
LEFT JOIN OrgHeader oh ON oh.oh_pk = oa.oa_oh
WHERE jda.e2_parentid = @id
    AND jda.e2_parenttablecode = 'JS'
    AND jda.e2_addresstype IN ('CRD', 'CEG');

SELECT t.*,
    t.jl_actualvolume AS pac_actual_volume,
    t.jl_packagecount AS pac_package_count,
    t.jl_rh_nkcommoditycode AS pac_commodity,
    COALESCE(NULLIF(t.jl_description, ''), t.jl_detaileddescription) AS pac_description
FROM JobPackLines t
WHERE t.jl_js = @id;

SELECT t.*
FROM JobDocumentData t
WHERE t.jdd_parentid = @id
    AND t.jdd_parenttablecode = 'SHP';

SELECT t.XV_Name, t.XV_Data
FROM GenCustomAddOnValue t
WHERE t.Xv_ParentID = @id;

SELECT
    c.jk_pk AS id,
    c.jk_uniqueconsignref AS reference,
    c.jk_rl_nkloadport AS first_load,
    c.jk_rl_nkdischargeport AS last_disc,
    c.jk_masterbillnum AS master_bill
FROM JobConShipLink l
INNER JOIN JobConsol c ON c.jk_pk = l.jn_jk
WHERE l.jn_js = @id
  AND c.jk_iscancelled = 0
ORDER BY c.jk_pk;
";

            ShipmentDetailOutput detail;
            List<JobDocAddressDtoOutput> addrs;
            List<OrgAddressWithHeaderDtoOutput> orgAddrs;
            List<ShipmentPackLineOutput> packLines;
            JobDocumentDataDtoOutput docData;
            List<GenCustomAddOnValueDtoOutput> customValues;
            List<ShipmentConsolidationOutput> consolidations;

            using (var multi = await _appSqlServerRepository.QueryMultipleAsync(sql, dp))
            {
                detail = await multi.ReadFirstOrDefaultAsync<ShipmentDetailOutput>();
                if (detail == null) return null;

                addrs = (await multi.ReadAsync<JobDocAddressDtoOutput>()).ToList();
                orgAddrs = (await multi.ReadAsync<OrgAddressWithHeaderDtoOutput>()).ToList();
                packLines = (await multi.ReadAsync<ShipmentPackLineOutput>()).ToList();
                docData = await multi.ReadFirstOrDefaultAsync<JobDocumentDataDtoOutput>();
                customValues = (await multi.ReadAsync<GenCustomAddOnValueDtoOutput>()).ToList();
                consolidations = (await multi.ReadAsync<ShipmentConsolidationOutput>()).ToList();
            }

            // 地址映射 - shipper 和 consignee 需要关联 OrgAddress
            var shipperTemp = addrs.FirstOrDefault(a => a.e2_addresstype == "CRD");
            if (shipperTemp != null && !string.IsNullOrEmpty(shipperTemp.e2_oa_address))
            {
                detail.shipperTemp = shipperTemp;
                detail.shipper = orgAddrs.FirstOrDefault(a => a.oa_pk == shipperTemp.e2_oa_address);
            }

            var consigneeTemp = addrs.FirstOrDefault(a => a.e2_addresstype == "CEG");
            if (consigneeTemp != null && !string.IsNullOrEmpty(consigneeTemp.e2_oa_address))
            {
                detail.consigneeTemp = consigneeTemp;
                detail.consignee = orgAddrs.FirstOrDefault(a => a.oa_pk == consigneeTemp.e2_oa_address);
            }

            detail.notify_party = addrs.FirstOrDefault(a => a.e2_addresstype == "NOTIFY_PARTY");
            detail.pickup = addrs.FirstOrDefault(a => a.e2_addresstype == "PICKUP");
            detail.delivery = addrs.FirstOrDefault(a => a.e2_addresstype == "DELIVERY");

            // 根据运输方式区分 FCL / 散货
            // FCL：集装箱 + 装箱明细经中间表 JobContainerPackPivot 平铺，每行一个集装箱携带其一条明细
            if (detail.js_transportmode == "SEA" && detail.js_packingmode == "FCL")
            {
                detail.containers_list = await QueryFclContainers(id);
            }
            else
            {
                detail.loose_list = packLines;
            }

            detail.doc_data = docData;
            detail.custom_values = customValues;
            detail.consolidation_list = consolidations;
            FillCustomFields(detail);

            return detail;
        }

        /// <summary>
        /// 把 GenCustomAddOnValue 的数组拍平成字典 + 具名字段。
        ///
        /// 前端反馈"返回的是个数组，里面找不到 Controlling Customer Full Name"，实际数据是在的
        /// （库里该名字有 53593 条），只是要按 XV_Name 在数组里找。这里直接给出字典和具名字段。
        ///
        /// 名字大小写在库里并不统一（'OP AT POL' 全大写、'Customer Service' 首字母大写），
        /// 所以字典用忽略大小写的比较器；Customer Service 另有拼写变体 'Coustomer Service'（10 条），
        /// 一并回退。
        /// </summary>
        private static void FillCustomFields(ShipmentDetailOutput detail)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in detail.custom_values)
            {
                var name = item.XV_Name?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;
                // 同名多行时保留第一条，避免后面的空值把有值的覆盖掉
                if (!map.ContainsKey(name))
                    map[name] = item.XV_Data;
            }

            detail.custom_fields = map;

            string Get(params string[] names)
            {
                foreach (var n in names)
                {
                    if (map.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v))
                        return v;
                }
                return null;
            }

            detail.controlling_customer = Get("Controlling Customer Full Name");
            detail.entrusting_party = Get("ENTRUSTING PARTY");
            detail.reject_release_reason = Get("Reject Release Reasons");
            detail.customer_service = Get("Customer Service", "Coustomer Service");
            detail.cs_email = Get("Customer Service Email");
            detail.op_at_pol = Get("OP AT POL");
            detail.op_at_pod = Get("OP AT POD");
            detail.sea_pricing = Get("SEA PRICING");
            detail.op_at_1st_booking_party = Get("OP AT 1ST BOOKING PARTY", "OP AT BOOKING PARTY");
            detail.op_at_2nd_booking_party = Get("OP AT 2ND BOOKING PARTY");
            detail.doc = Get("DOC");
        }

        /// <summary>
        /// FCL：装箱明细(JobPackLines，货物) + 集装箱(JobContainer)，经中间表 JobContainerPackPivot 平铺。
        /// 每行 = 一条货物明细携带其所属集装箱（J6_JL = jl_pk，J6_JC = jc_pk）；
        /// 一个集装箱含两条货物明细 => 返回两行，两行的集装箱相同。
        ///
        /// 这条三表 join 的结果只在 SEA + FCL 时使用，故从 Detail 的批量查询里拆出来单独按需执行，
        /// 空运和散货的详情请求不必再为它付代价。
        /// </summary>
        private async Task<List<ShipmentContainerOutput>> QueryFclContainers(string id)
        {
            var dp = new DynamicParameters();
            dp.Add("id", id, DbType.AnsiString);

            using var multi = await _appSqlServerRepository.QueryMultipleAsync(@"
WITH CandidateContainers AS
(
    SELECT jc.jc_pk
    FROM JobContainer jc
    WHERE jc.jc_js_fclbookingonlylink = @id

    UNION

    SELECT p.J6_JC
    FROM JobContainerPackPivot p
    INNER JOIN JobPackLines jl ON jl.jl_pk = p.J6_JL
    WHERE jl.jl_js = @id

    UNION

    SELECT jc.jc_pk
    FROM JobContainer jc
    INNER JOIN JobConShipLink csl ON csl.jn_jk = jc.jc_jk
    WHERE csl.jn_js = @id
)
SELECT jl.*,
    agg.total_volume AS pac_actual_volume,
    agg.pack_count AS pac_package_count,
    jl.jl_rh_nkcommoditycode AS pac_commodity,
    COALESCE(NULLIF(jl.jl_description, ''), jl.jl_detaileddescription) AS pac_description,
    jc.*,
    -- RC_Code 是定长 char，不 RTRIM 会带出 '40HC      ' 这种尾随空格
    RTRIM(rc.RC_Code) AS container_type_code,
    rc.RC_Description AS container_type_desc,
    agg.pack_count,
    agg.pack_type,
    agg.total_volume,
    agg.total_weight
FROM CandidateContainers cc
INNER JOIN JobContainer jc ON jc.jc_pk = cc.jc_pk
OUTER APPLY (
    SELECT TOP 1 jl.*
    FROM JobContainerPackPivot p
    INNER JOIN JobPackLines jl ON jl.jl_pk = p.J6_JL
    WHERE p.J6_JC = jc.jc_pk
      AND jl.jl_js = @id
    ORDER BY p.J6_JL
) jl
LEFT JOIN RefContainer rc ON rc.RC_PK = jc.jc_rc
-- 箱级汇总：本行只带该箱其中一条明细，整箱件数/体积/毛重要把该箱所有明细加起来
OUTER APPLY (
    SELECT SUM(jl2.jl_packagecount)  AS pack_count,
           MAX(jl2.jl_f3_nkpacktype) AS pack_type,
           SUM(jl2.jl_actualvolume)  AS total_volume,
           SUM(jl2.jl_actualweight)  AS total_weight
    FROM JobContainerPackPivot p2
    INNER JOIN JobPackLines jl2 ON jl2.jl_pk = p2.J6_JL
    WHERE p2.J6_JC = jc.jc_pk
      AND jl2.jl_js = @id
) agg
;
", dp);

            // 平铺读取：jl.* 映射到货物明细，jc_pk 起的列映射到 container
            return multi.Read<ShipmentContainerOutput, ShipmentContainerInfoOutput, ShipmentContainerOutput>(
                (l, c) => { l.container = c; return l; }, splitOn: "jc_pk").ToList();
        }

        public async Task<ShipmentQueryConsolTransportOutput> QueryConsolTransport(ShipmentQueryConsolTransportInput input)
        {
            var output = new ShipmentQueryConsolTransportOutput();
            input ??= new ShipmentQueryConsolTransportInput();
            if (string.IsNullOrWhiteSpace(input.shp_pk))
            {
                return output;
            }

            var list = await _appSqlServerRepository.QueryAsync<ShipmentQueryConsolTransportDto>(@"
SELECT
  c.jk_uniqueconsignref,
  t.*
FROM
  JobConShipLink l
  INNER JOIN JobConsol c ON c.jk_pk = l.jn_jk
  INNER JOIN JobConsolTransport t ON t.jw_parentguid = c.jk_pk
WHERE
  1 = 1
  AND l.jn_js = @shp_pk
  AND t.jw_isvalid = 1
  AND t.jw_parenttype = 'CON'
ORDER BY
  t.jw_legorder ASC
", new { input.shp_pk });

            output.list = list.ToList();
            return output;
        }
    }
}
