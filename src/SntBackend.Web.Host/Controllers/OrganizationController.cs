using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Organization;
using SntBackend.Application.Organization.Dto;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// 组织前端控制器
/// </summary>
[Route("organization")]
public class OrganizationController : SntBackendControllerBase
{
    private readonly IOrganizationApplication _organizationApplication;

    public OrganizationController(IOrganizationApplication organizationApplication)
    {
        _organizationApplication = organizationApplication;
    }

    /// <summary>
    /// 通用组织地址下拉数据源（可作 Debtor 下拉），分页 + query / address_type 过滤。
    /// </summary>
    [HttpGet]
    [Route("query-org-address")] 
    public async Task<JsonResponse<OrgAddressQueryOutput>> QueryOrgAddress([FromQuery] OrgAddressQueryInput input)
    {
        var data = await _organizationApplication.QueryOrgAddress(input);
        return new JsonResponse<OrgAddressQueryOutput> { Data = data };
    }
}
