using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Shipment;
using SntBackend.Application.Shipment.Dto;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;
using Facade.AspNetCore.Mvc.Authorization;

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

    /// <summary>
    /// 查询详情
    /// </summary>
    [HttpGet]
    [Route("detail")]
    public async Task<JsonResponse<ShipmentDetailOutput>> Detail([FromQuery] string id)
    {
        var result = await _shipmentApplication.Detail(id);
        if (result == null)
        {
            return new JsonResponse<ShipmentDetailOutput>(false, "Shipment not found.");
        }
        return new JsonResponse<ShipmentDetailOutput> { Data = result };
    }
}
