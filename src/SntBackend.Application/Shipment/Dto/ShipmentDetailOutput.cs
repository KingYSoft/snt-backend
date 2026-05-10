using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Shipment.Dto
{
    public class ShipmentDetailOutput : JobShipmentDtoOutput
    {
        public JobDocAddressDtoOutput shipperTemp { get; set; }
        public OrgAddressWithHeaderDtoOutput shipper { get; set; }
        
        public JobDocAddressDtoOutput consigneeTemp { get; set; }
        public OrgAddressWithHeaderDtoOutput consignee { get; set; }
        
        public JobDocAddressDtoOutput notify_party { get; set; }
        public JobDocAddressDtoOutput pickup { get; set; }
        public JobDocAddressDtoOutput delivery { get; set; }

        public List<ShipmentDetailContainerDto> containers_list { get; set; } = new();
        public List<JobPackLinesDtoOutput> loose_list { get; set; } = new();
        public JobDocumentDataDtoOutput doc_data { get; set; }
        
        /// <summary>
        /// Carrier 名称（从 OrgHeader.oh_fullname 获取）
        /// </summary>
        public string carrier_name { get; set; }
        
        /// <summary>
        /// Booking Party 名称（从 OrgHeader.oh_fullname 获取）
        /// </summary>
        public string booking_party_name { get; set; }
    }
    
    /// <summary>
    /// OrgAddress 扩展 DTO（包含 OrgHeader 信息）
    /// </summary>
    public class OrgAddressWithHeaderDtoOutput : OrgAddressDtoOutput
    {
        public string oh_fullname { get; set; }
    }

    public class ShipmentDetailContainerDto : JobContainerDtoOutput
    {
        public string jl_rh_nkcommoditycode { get; set; }
        public decimal? jl_actualweight { get; set; }
        public decimal? jl_actualvolume { get; set; }
        public int? jl_packagecount { get; set; }
        public string jl_f3_nkpacktype { get; set; }
        public string jl_description { get; set; }
        
        /// <summary>
        /// Container Code（从 RefContainer.RC_Code 获取）
        /// </summary>
        public string rc_code { get; set; }
    }
}
