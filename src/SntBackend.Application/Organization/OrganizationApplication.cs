using Dapper;
using SntBackend.Application.Organization.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Organization
{
    /// <summary>
    /// 通用组织地址下拉（移植自 first-cargo OrganizationApplication.QueryOrgAddress，按 snt 表名/字段重写）。
    ///
    /// 数据模型差异：first-cargo 用 org_detail.is_debtor 判定 DEBTOR/CREDITOR；snt 无 org_detail 表，
    /// OrgHeader 也无 debtor 标志位，故 DEBTOR/CREDITOR 退化为“全部启用组织”。
    /// 可用标志位仅 oh_isconsignor / oh_isconsignee / oh_isforwarder。
    /// </summary>
    public class OrganizationApplication : SntBackendApplicationBase, IOrganizationApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public OrganizationApplication(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

        public async Task<OrgAddressQueryOutput> QueryOrgAddress(OrgAddressQueryInput input)
        {
            input ??= new OrgAddressQueryInput();
            var skipCount = Math.Max(input.SkipCount, 0);
            var maxResultCount = input.MaxResultCount <= 0
                ? 50
                : Math.Min(input.MaxResultCount, 200);

            var dp = new DynamicParameters();
            dp.Add("skipCount", skipCount);
            dp.Add("maxResultCount", maxResultCount);

            var whereIf = string.Empty;
            if (!string.IsNullOrWhiteSpace(input.query))
            {
                whereIf += " AND (h.oh_code LIKE @query OR h.oh_fullname LIKE @query) ";
                dp.Add("query", $"{input.query.Trim()}%");
            }

            switch ((input.address_type ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "SHIPPER":
                    whereIf += " AND h.oh_isconsignor = 1 ";
                    break;
                case "CONSIGNEE":
                    whereIf += " AND h.oh_isconsignee = 1 ";
                    break;
                case "LOCAL_AGENT":
                case "OVERSEAS_AGENT":
                    whereIf += " AND h.oh_isforwarder = 1 ";
                    break;
                default:
                    // DEBTOR / CREDITOR / DELIVERY / PICKUP / AGENT / 空 => 全部启用组织
                    break;
            }

            var orderBy = "ORDER BY h.oh_code ASC";
            if (!string.IsNullOrWhiteSpace(input.query))
            {
                orderBy = "ORDER BY (CASE WHEN h.oh_code = @queryCode THEN 1 ELSE 0 END) DESC, h.oh_code ASC";
                dp.Add("queryCode", input.query.Trim());
            }

            var totalSql = $@"
SELECT COUNT(1)
FROM OrgHeader h
WHERE h.oh_isactive = 1
    {whereIf}";

            // 每个组织取一条启用地址（OUTER APPLY，保证下拉一行一个组织）。
            var pageSql = $@"
SELECT
    h.oh_pk,
    h.oh_code,
    h.oh_fullname,
    a.oa_pk,
    a.oa_code,
    a.oa_companynameoverride,
    a.oa_address1,
    a.oa_address2,
    a.oa_city,
    a.oa_state,
    a.oa_postcode,
    a.oa_rn_nkcountrycode,
    a.oa_phone,
    a.oa_email
FROM OrgHeader h
OUTER APPLY (
    SELECT TOP 1 oa.*
    FROM OrgAddress oa
    WHERE oa.oa_oh = h.oh_pk AND oa.oa_isactive = 1
    ORDER BY oa.oa_pk
) a
WHERE h.oh_isactive = 1
    {whereIf}
{orderBy}
OFFSET @skipCount ROWS FETCH NEXT @maxResultCount ROWS ONLY";

            var total = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(totalSql, dp);
            var list = (await _appSqlServerRepository.QueryAsync<OrgAddressQueryDto>(pageSql, dp)).ToList();

            return new OrgAddressQueryOutput
            {
                TotalCount = total,
                Items = list
            };
        }
    }
}
