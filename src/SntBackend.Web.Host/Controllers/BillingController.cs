using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Billing;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// 应收应付账单前端控制器
/// </summary>
[Route("billing")]
public class BillingController : SntBackendControllerBase
{
    private readonly IBillingApplication _billingApplication;

    public BillingController(IBillingApplication billingApplication)
    {
        _billingApplication = billingApplication;
    }

    /// <summary>
    /// AP 应付账单分页查询
    /// </summary>
    [HttpPost]
    [Route("ap/tbl")]
    public async Task<JsonResponse<BillingTblOutput>> ApTbl([FromBody] BillingTblInput input)
    {
        var result = await _billingApplication.ApTbl(input);
        return new JsonResponse<BillingTblOutput> { Data = result };
    }

    /// <summary>
    /// AR 应收账单分页查询
    /// </summary>
    [HttpPost]
    [Route("ar/tbl")]
    public async Task<JsonResponse<BillingTblOutput>> ArTbl([FromBody] BillingTblInput input)
    {
        var result = await _billingApplication.ArTbl(input);
        return new JsonResponse<BillingTblOutput> { Data = result };
    }

    /// <summary>
    /// 账单详情
    /// </summary>
    [HttpGet]
    [Route("detail")]
    public async Task<JsonResponse<AccTransactionHeaderDtoOutput>> Detail([FromQuery] string id)
    {
        var result = await _billingApplication.Detail(id);
        if (result == null)
        {
            return new JsonResponse<AccTransactionHeaderDtoOutput>(false, "Transaction not found.");
        }
        return new JsonResponse<AccTransactionHeaderDtoOutput> { Data = result };
    }
}
