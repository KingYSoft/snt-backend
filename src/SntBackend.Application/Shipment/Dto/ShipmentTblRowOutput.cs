using SntBackend.Application.Po.Dto;

namespace SntBackend.Application.Shipment.Dto
{
    public class ShipmentTblRowOutput : JobShipmentDtoOutput
    {
        public string con_master_bill_num { get; set; }
        public string shipper_name { get; set; }
        public string consignee_name { get; set; }
        public string shp_cargo_ready { get; set; }
        public string shp_freight_terms { get; set; }
        public string shp_delivery_mode { get; set; }
        public string pac_container_type { get; set; }
        public int? pac_container_count { get; set; }
        public string pac_container_num { get; set; }
        public string carrier_name { get; set; }
        public string con_vessel { get; set; }
        public string con_voyage { get; set; }
        public string shp_job_rep_sales_name { get; set; }
    }
}
