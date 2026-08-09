using Dapper;
using SntBackend.Application.Po.Dto;
using SntBackend.Application.Shipment.Dto;
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
";

            ShipmentDetailOutput detail;
            List<JobDocAddressDtoOutput> addrs;
            List<OrgAddressWithHeaderDtoOutput> orgAddrs;
            List<JobPackLinesDtoOutput> packLines;
            JobDocumentDataDtoOutput docData;
            List<GenCustomAddOnValueDtoOutput> customValues;

            using (var multi = await _appSqlServerRepository.QueryMultipleAsync(sql, dp))
            {
                detail = await multi.ReadFirstOrDefaultAsync<ShipmentDetailOutput>();
                if (detail == null) return null;

                addrs = (await multi.ReadAsync<JobDocAddressDtoOutput>()).ToList();
                orgAddrs = (await multi.ReadAsync<OrgAddressWithHeaderDtoOutput>()).ToList();
                packLines = (await multi.ReadAsync<JobPackLinesDtoOutput>()).ToList();
                docData = await multi.ReadFirstOrDefaultAsync<JobDocumentDataDtoOutput>();
                customValues = (await multi.ReadAsync<GenCustomAddOnValueDtoOutput>()).ToList();
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
SELECT jl.*, jc.*,
    -- RC_Code 是定长 char，不 RTRIM 会带出 '40HC      ' 这种尾随空格
    RTRIM(rc.RC_Code) AS container_type_code,
    rc.RC_Description AS container_type_desc,
    agg.pack_count,
    agg.pack_type,
    agg.total_volume,
    agg.total_weight
FROM JobPackLines jl
INNER JOIN JobContainerPackPivot p ON p.J6_JL = jl.jl_pk
INNER JOIN JobContainer jc ON jc.jc_pk = p.J6_JC
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
) agg
WHERE jl.jl_js = @id;
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
