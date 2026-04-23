using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Consolidation;
using SntBackend.Application.Consolidation.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// Consolidation
/// </summary>
[Route("consolidation")]
public class ConsolidationController : SntBackendControllerBase
{
    private readonly IConsolidationApplication _consolidationApplication;

    public ConsolidationController(IConsolidationApplication consolidationApplication)
    {
        _consolidationApplication = consolidationApplication;
    }

    /// <summary>
    /// 分页查询
    /// </summary>
    [HttpPost]
    [Route("tbl")]
    public async Task<JsonResponse<ConsolidationTblOutput>> Tbl([FromBody] ConsolidationTblInput input)
    {
        var result = await _consolidationApplication.Tbl(input);
        return new JsonResponse<ConsolidationTblOutput> { Data = result };
    }

    /// <summary>
    /// 查询详情
    /// </summary>
    [HttpGet]
    [Route("detail")]
    public async Task<JsonResponse<JobConsolDtoOutput>> Detail([FromQuery] string id)
    {
        var result = await _consolidationApplication.Detail(id);
        if (result == null)
        {
            return new JsonResponse<JobConsolDtoOutput>(false, "Consolidation not found.");
        }
        return new JsonResponse<JobConsolDtoOutput> { Data = result };
    }
}
