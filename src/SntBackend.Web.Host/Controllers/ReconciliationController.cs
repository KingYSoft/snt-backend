using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Billing;
using SntBackend.Application.Billing.Dto;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// 销账管理前端控制器
/// </summary>
[Route("reconciliation")]
public class ReconciliationController : SntBackendControllerBase
{
    private readonly IBillingApplication _billingApplication;

    public ReconciliationController(IBillingApplication billingApplication)
    {
        _billingApplication = billingApplication;
    }

    /// <summary>
    /// 销账分页查询
    /// </summary>
    [HttpPost]
    [Route("writeoff/tbl")]
    public async Task<JsonResponse<WriteOffTblOutput>> WriteOffTbl([FromBody] WriteOffTblInput input)
    {
        var result = await _billingApplication.WriteOffTbl(input);
        return new JsonResponse<WriteOffTblOutput> { Data = result };
    }

    /// <summary>
    /// 销账详情（含 MatchLink 明细）
    /// </summary>
    [HttpGet]
    [Route("writeoff/detail")]
    public async Task<JsonResponse<WriteOffDetailOutput>> WriteOffDetail([FromQuery] WriteOffDetailInput input)
    {
        var result = await _billingApplication.WriteOffDetail(input);
        if (result == null)
        {
            return new JsonResponse<WriteOffDetailOutput>(false, "Write-off record not found.");
        }
        return new JsonResponse<WriteOffDetailOutput> { Data = result };
    }
}
