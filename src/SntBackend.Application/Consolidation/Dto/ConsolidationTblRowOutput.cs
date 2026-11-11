using SntBackend.Application.Po.Dto;

namespace SntBackend.Application.Consolidation.Dto
{
    public class ConsolidationTblRowOutput : JobConsolDtoOutput
    {
        public System.DateTime? con_etd { get; set; }
        public System.DateTime? con_eta { get; set; }
        public string con_vessel { get; set; }
        public string con_voyage { get; set; }
        public string con_carrier_name { get; set; }
        public string ctr_type { get; set; }
        public int? ctr_count { get; set; }
        public string jh_gc { get; set; }
        public string company_name { get; set; }
        public string jh_gs_nkrepsales { get; set; }
        public string sales_name { get; set; }
        public string jh_gs_nkrepops { get; set; }
        public string operation_name { get; set; }
        public string customer_service { get; set; }
        public string customer_service_name { get; set; }
    }
}
