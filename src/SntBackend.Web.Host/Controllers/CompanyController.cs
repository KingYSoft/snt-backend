using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Company;
using SntBackend.Application.Company.Dto;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.Authorization;
using SntBackend.Web.Core.Authorization;
using SntBackend.Web.Core.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// 公司/汇率前端控制器
/// </summary>
[Route("company")]
public class CompanyController : SntBackendControllerBase
{
    private readonly ICompanyApplication _companyApplication;
    private readonly SystemPermissionChecker _permissionChecker;

    public CompanyController(
        ICompanyApplication companyApplication,
        SystemPermissionChecker permissionChecker)
    {
        _companyApplication = companyApplication;
        _permissionChecker = permissionChecker;
    }

    [HttpPost]
    [Route("query-page")]
    public async Task<JsonResponse<Abp.Application.Services.Dto.PagedResultDto<CompanyDtoOutput>>> QueryPage(
        [FromBody] CompanyQueryInput input)
    {
        return new JsonResponse<Abp.Application.Services.Dto.PagedResultDto<CompanyDtoOutput>>
        {
            Data = await _companyApplication.QueryPage(input)
        };
    }

    [HttpPost]
    [Route("save")]
    public async Task<JsonResponse<CompanyDtoOutput>> Save([FromBody] CompanySaveInput input)
    {
        await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemCompany);
        return new JsonResponse<CompanyDtoOutput>
        {
            Data = await _companyApplication.Save(input)
        };
    }

    [HttpPost]
    [Route("delete/{pk}")]
    public async Task<JsonResponse> Delete(string pk)
    {
        await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemCompany);
        await _companyApplication.Delete(pk);
        return new JsonResponse();
    }

    /// <summary>
    /// 查询卖出汇率（ZZRefExchangeRate.re_sellrate）。
    /// </summary>
    [HttpPost]
    [Route("get")] 
    public async Task<JsonResponse<List<CompanyQueryRateOutput>>> Get([FromBody] GetCompanyInput input)
    {
        var data = await _companyApplication.Get(input);
        return new JsonResponse<List<CompanyQueryRateOutput>> { Data = data };
    }
}
