using Abp.Application.Services.Dto;

namespace SntBackend.Application.Organization.Dto
{
    /// <summary>
    /// 通用组织地址下拉查询入参（对齐 first-cargo api/organization/query-org-address）。
    /// 分页字段来自 PagedAndSortedResultRequestDto：SkipCount / MaxResultCount。
    /// </summary>
    public class OrgAddressQueryInput : PagedAndSortedResultRequestDto
    {
        /// <summary>按 组织代码 / 全名 前缀模糊匹配。</summary>
        public string query { get; set; }

        /// <summary>
        /// 地址类型过滤：SHIPPER(oh_isconsignor) / CONSIGNEE(oh_isconsignee) /
        /// LOCAL_AGENT|OVERSEAS_AGENT(oh_isforwarder)。
        /// DEBTOR/CREDITOR 等 snt 无对应标志位，按全部启用组织返回。
        /// </summary>
        public string address_type { get; set; }
    }
}
