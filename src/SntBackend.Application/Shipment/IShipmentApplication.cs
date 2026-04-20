using SntBackend.Application.Po.Dto;
using SntBackend.Application.Shipment.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.Shipment
{
    public interface IShipmentApplication : ISntBackendApplicationBase
    {
        Task<ShipmentTblOutput> Tbl(ShipmentTblInput input);
        Task<JobShipmentDtoOutput> Detail(string id);
    }
}
