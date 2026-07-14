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

        public List<ShipmentContainerOutput> containers_list { get; set; } = new();
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
    /// 集装箱（JobContainer）+ 该行对应的装箱明细（JobPackLines）
    /// 经中间表 JobContainerPackPivot 平铺：每行 = 一个集装箱携带其一条明细
    /// （J6_JC = JC_PK，J6_JL = JL_PK）
    /// </summary>
    public class ShipmentContainerOutput : JobContainerDtoOutput
    {
        public JobPackLinesDtoOutput pack_line { get; set; }
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
