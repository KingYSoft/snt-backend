using SntBackend.Application.Organization.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.Organization
{
    public interface IOrganizationApplication : ISntBackendApplicationBase
    {
        /// <summary>
        /// 通用组织地址下拉数据源（OrgHeader + OrgAddress），分页 + 按 query / address_type 过滤。
        /// </summary>
        Task<OrgAddressQueryOutput> QueryOrgAddress(OrgAddressQueryInput input);
    }
}
