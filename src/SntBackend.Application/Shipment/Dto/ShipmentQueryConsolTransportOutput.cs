using System.Collections.Generic;
using SntBackend.Application.Po.Dto;

namespace SntBackend.Application.Shipment.Dto
{
    public class ShipmentQueryConsolTransportOutput
    {
        public List<ShipmentQueryConsolTransportDto> list { get; set; } = new List<ShipmentQueryConsolTransportDto>();
    }

    public class ShipmentQueryConsolTransportDto : JobConsolTransportDtoOutput
    {
        public string jk_uniqueconsignref { get; set; }
    }
}
