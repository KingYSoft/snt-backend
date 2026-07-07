using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Company;
using SntBackend.Application.Company.Dto;
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

    public CompanyController(ICompanyApplication companyApplication)
    {
        _companyApplication = companyApplication;
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
