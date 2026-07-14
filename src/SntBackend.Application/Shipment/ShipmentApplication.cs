using Dapper;
using SntBackend.Application.Po.Dto;
using SntBackend.Application.Shipment.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Shipment
{
    public class ShipmentApplication : SntBackendApplicationBase, IShipmentApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public ShipmentApplication(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
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

        public async Task<ShipmentTblOutput> Tbl(ShipmentTblInput input)
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
SELECT t.*
FROM JobShipment t
WHERE 1 = 1
    AND t.js_iscancelled = 0 
    {whereIf}
ORDER BY t.js_pk desc
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
                var list = (await multi.ReadAsync<JobShipmentDtoOutput>()).ToList();

                output.TotalCount = total;
                output.Items = list;
            }

            return output;
        }

        public async Task<ShipmentDetailOutput> Detail(string id)
        {
            var dp = new DynamicParameters();
            dp.Add("id", id);

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

SELECT t.*
FROM JobPackLines t
WHERE t.jl_js = @id;

SELECT t.*
FROM JobDocumentData t
WHERE t.jdd_parentid = @id
    AND t.jdd_parenttablecode = 'SHP';

SELECT t.XV_Name, t.XV_Data
FROM GenCustomAddOnValue t
WHERE t.Xv_ParentID = @id;

-- FCL：集装箱(JobContainer) + 装箱明细(JobPackLines)，经中间表 JobContainerPackPivot 平铺
-- 每行 = 一个集装箱携带其一条明细（J6_JC = jc_pk，J6_JL = jl_pk）
SELECT jc.*, jl.*
FROM JobPackLines jl
INNER JOIN JobContainerPackPivot p ON p.J6_JL = jl.jl_pk
INNER JOIN JobContainer jc ON jc.jc_pk = p.J6_JC
WHERE jl.jl_js = @id;
";

            using var multi = await _appSqlServerRepository.QueryMultipleAsync(sql, dp);

            var detail = await multi.ReadFirstOrDefaultAsync<ShipmentDetailOutput>();
            if (detail == null) return null;

            var addrs = (await multi.ReadAsync<JobDocAddressDtoOutput>()).ToList();
            var orgAddrs = (await multi.ReadAsync<OrgAddressWithHeaderDtoOutput>()).ToList();
            var packLines = (await multi.ReadAsync<JobPackLinesDtoOutput>()).ToList();
            var docData = await multi.ReadFirstOrDefaultAsync<JobDocumentDataDtoOutput>();
            var customValues = (await multi.ReadAsync<GenCustomAddOnValueDtoOutput>()).ToList();
            // 平铺读取：jc.* 映射到集装箱，jl_pk 起的列映射到 pack_line
            var containers = multi.Read<ShipmentContainerOutput, JobPackLinesDtoOutput, ShipmentContainerOutput>(
                (c, l) => { c.pack_line = l; return c; }, splitOn: "jl_pk").ToList();

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
                detail.containers_list = containers;
            }
            else
            {
                detail.loose_list = packLines;
            }

            detail.doc_data = docData;
            detail.custom_values = customValues;

            return detail;
        }
    }
}
