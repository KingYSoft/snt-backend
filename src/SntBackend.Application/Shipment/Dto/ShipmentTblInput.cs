using System.Collections.Generic;

namespace SntBackend.Application.Shipment.Dto
{
    public class ShipmentTblInput
    {
        public int SkipCount { get; set; }
        public int MaxResultCount { get; set; } = 20;
        public List<ShipmentTblFilterItem> filters { get; set; } = new();
    }

    public class ShipmentTblFilterItem
    {
        public string key { get; set; }
        public string op { get; set; }
        public string val { get; set; }
        public string start { get; set; }
        public string end { get; set; }
    }
}
