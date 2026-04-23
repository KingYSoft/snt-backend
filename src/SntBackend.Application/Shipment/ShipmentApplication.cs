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
    AND t.js_isforwardregistered = 1
    {whereIf}
";
            var pageSql = @$"
SELECT t.*
FROM JobShipment t
WHERE 1 = 1
    AND t.js_iscancelled = 0
    AND t.js_isforwardregistered = 1
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
SELECT t.*
FROM JobShipment t
WHERE t.js_pk = @id;

SELECT t.*
FROM JobDocAddress t
WHERE t.e2_parentid = @id
    AND t.e2_parenttablecode = 'SHP';

SELECT t.*
FROM JobContainer t
WHERE t.jc_js_fclbookingonlylink = @id;

SELECT t.*
FROM JobPackLines t
WHERE t.jl_js = @id;

SELECT t.*
FROM JobDocumentData t
WHERE t.jdd_parentid = @id
    AND t.jdd_parenttablecode = 'SHP';
";

            using var multi = await _appSqlServerRepository.QueryMultipleAsync(sql, dp);

            var detail = await multi.ReadFirstOrDefaultAsync<ShipmentDetailOutput>();
            if (detail == null) return null;

            var addrs = (await multi.ReadAsync<JobDocAddressDtoOutput>()).ToList();
            var containers = (await multi.ReadAsync<ShipmentDetailContainerDto>()).ToList();
            var packLines = (await multi.ReadAsync<JobPackLinesDtoOutput>()).ToList();
            var docData = await multi.ReadFirstOrDefaultAsync<JobDocumentDataDtoOutput>();

            // 地址映射
            detail.shipper = addrs.FirstOrDefault(a => a.e2_addresstype == "SHIPPER");
            detail.consignee = addrs.FirstOrDefault(a => a.e2_addresstype == "CONSIGNEE");
            detail.notify_party = addrs.FirstOrDefault(a => a.e2_addresstype == "NOTIFY_PARTY");
            detail.pickup = addrs.FirstOrDefault(a => a.e2_addresstype == "PICKUP");
            detail.delivery = addrs.FirstOrDefault(a => a.e2_addresstype == "DELIVERY");

            // 根据运输方式区分 FCL / 散货
            if (detail.js_transportmode == "SEA" && detail.js_packingmode == "FCL")
            {
                detail.containers_list = containers;
                foreach (var ctr in detail.containers_list)
                {
                    var pl = packLines.FirstOrDefault(p => p.jl_js == detail.js_pk);
                    if (pl != null)
                    {
                        ctr.jl_rh_nkcommoditycode = pl.jl_rh_nkcommoditycode;
                        ctr.jl_actualweight = pl.jl_actualweight;
                        ctr.jl_actualvolume = pl.jl_actualvolume;
                        ctr.jl_packagecount = pl.jl_packagecount;
                        ctr.jl_f3_nkpacktype = pl.jl_f3_nkpacktype;
                        ctr.jl_description = pl.jl_description;
                    }
                }
            }
            else
            {
                detail.loose_list = packLines;
            }

            detail.doc_data = docData;

            return detail;
        }
    }
}
