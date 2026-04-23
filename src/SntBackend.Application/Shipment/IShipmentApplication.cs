using SntBackend.Application.Shipment.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.Shipment
{
    public interface IShipmentApplication : ISntBackendApplicationBase
    {
        Task<ShipmentTblOutput> Tbl(ShipmentTblInput input);
        Task<ShipmentDetailOutput> Detail(string id);
    }
}
