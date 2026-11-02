using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Shipment;
using SntBackend.Application.Shipment.Dto;
using SntBackend.Application.Excel;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;
using Facade.AspNetCore.Mvc.Authorization;
using System;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// Shipment
/// </summary>
[Route("shipment")]
public class ShipmentController : SntBackendControllerBase
{
    private readonly IShipmentApplication _shipmentApplication;

    public ShipmentController(IShipmentApplication shipmentApplication)
    {
        _shipmentApplication = shipmentApplication;
    }

    /// <summary>
    /// 分页查询
    /// </summary>
    [HttpPost]
    [Route("tbl")]
    [NoToken]
    public async Task<JsonResponse<ShipmentTblOutput>> Tbl([FromBody] ShipmentTblInput input)
    {
        var result = await _shipmentApplication.Tbl(input);
        return new JsonResponse<ShipmentTblOutput> { Data = result };
    }

    [HttpPost]
    [Route("export")]
    [NoToken]
    public async Task<IActionResult> Export([FromBody] ShipmentTblInput input)
    {
        var bytes = await _shipmentApplication.Export(input);
        return File(bytes, SjcExcelExportHelper.ContentType, "Shipments.xlsx");
    }

    /// <summary>
    /// 查询详情
    /// </summary>
    [HttpGet]
    [Route("detail")]
    public async Task<JsonResponse<ShipmentDetailOutput>> Detail([FromQuery] string id)
    {
        try
        {
            var result = await _shipmentApplication.Detail(id);
            if (result == null)
            {
                return new JsonResponse<ShipmentDetailOutput>(false, "Shipment not found.");
            }
            return new JsonResponse<ShipmentDetailOutput> { Data = result };
        }
        catch (Exception ex)
        {
            return new JsonResponse<ShipmentDetailOutput>(false, $"错误: {ex.Message}\n\n堆栈: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 查询 Consol 运输路线（JobConsolTransport）
    /// </summary>
    [HttpGet]
    [Route("query-consol-transport")]
    public async Task<JsonResponse<ShipmentQueryConsolTransportOutput>> QueryConsolTransport([FromQuery] ShipmentQueryConsolTransportInput input)
    {
        var result = await _shipmentApplication.QueryConsolTransport(input);
        return new JsonResponse<ShipmentQueryConsolTransportOutput> { Data = result };
    }
}
