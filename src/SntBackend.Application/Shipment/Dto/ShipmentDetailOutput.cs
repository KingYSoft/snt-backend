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

        public List<JobPackLinesDtoOutput> containers_list { get; set; } = new();
        public List<JobPackLinesDtoOutput> loose_list { get; set; } = new();
        public JobDocumentDataDtoOutput doc_data { get; set; }

        /// <summary>
        /// 自定义附加字段（GenCustomAddOnValue，按 JS_PK 关联）
        /// </summary>
        public List<GenCustomAddOnValueDtoOutput> custom_values { get; set; } = new();

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

    /// <summary>
    /// 自定义附加字段（GenCustomAddOnValue 表）
    /// </summary>
    public class GenCustomAddOnValueDtoOutput
    {
        public string XV_Name { get; set; }
        public string XV_Data { get; set; }
    }
}
