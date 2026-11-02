using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Shipment.Dto
{
    public class ShipmentTblOutput
    {
        public int TotalCount { get; set; }
        public List<ShipmentTblRowOutput> Items { get; set; } = new();
    }
}
