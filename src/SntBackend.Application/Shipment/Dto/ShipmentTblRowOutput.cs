using SntBackend.Application.Po.Dto;
using System.Text.Json.Serialization;

namespace SntBackend.Application.Shipment.Dto
{
    public class ShipmentTblRowOutput : JobShipmentDtoOutput
    {
        public string con_master_bill_num { get; set; }
        [JsonIgnore]
        public string linked_consol_reference { get; set; }
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
        public string jh_gc { get; set; }
        public string company_name { get; set; }
        public string jh_gs_nkrepsales { get; set; }
        public string sales_name { get; set; }
        public string jh_gs_nkrepops { get; set; }
        public string operation_name { get; set; }
        public string customer_service { get; set; }
        public string customer_service_name { get; set; }
        public string shp_job_rep_sales_name { get; set; }
    }
}
