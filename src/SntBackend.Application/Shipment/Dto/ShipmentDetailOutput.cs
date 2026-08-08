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
        /// 自定义附加字段的字典形式：key = XV_Name，value = XV_Data。
        /// 与 <see cref="custom_values"/> 同一份数据，只是免去前端在数组里按名字找。
        /// 库里实际存在的 key（按出现次数）：Contract No.、SEA PRICING、Customer Service、
        /// Controlling Customer Full Name、OP AT 1ST BOOKING PARTY、DOC、ENTRUSTING PARTY、
        /// OP AT 2ND BOOKING PARTY、Cargo Ready Date、PRICING、Reject Release Reasons、
        /// OP AT POL、OP AT BOOKING PARTY、OP AT POD、Customer Service Email、Coustomer Service。
        /// 注意这些名字大小写不统一（如 OP AT POL 全大写、Customer Service 首字母大写），
        /// 且存在拼写变体 Coustomer Service，所以另外提供了下面几个具名字段。
        /// </summary>
        public Dictionary<string, string> custom_fields { get; set; } = new();

        /// <summary>Controlling Customer：custom_values 里 XV_Name = 'Controlling Customer Full Name'</summary>
        public string controlling_customer { get; set; }

        /// <summary>Entrusting Party：XV_Name = 'ENTRUSTING PARTY'</summary>
        public string entrusting_party { get; set; }

        /// <summary>Reject Release Reason：XV_Name = 'Reject Release Reasons'</summary>
        public string reject_release_reason { get; set; }

        /// <summary>Customer Service：XV_Name = 'Customer Service'，兼容库里的拼写变体 'Coustomer Service'</summary>
        public string customer_service { get; set; }

        /// <summary>CS Email：XV_Name = 'Customer Service Email'</summary>
        public string cs_email { get; set; }

        /// <summary>OP AT POL：XV_Name = 'OP AT POL'</summary>
        public string op_at_pol { get; set; }

        /// <summary>OP AT POD：XV_Name = 'OP AT POD'</summary>
        public string op_at_pod { get; set; }

        /// <summary>SEA PRICING：XV_Name = 'SEA PRICING'</summary>
        public string sea_pricing { get; set; }

        /// <summary>OP AT 1ST Booking Party：XV_Name = 'OP AT 1ST BOOKING PARTY'</summary>
        public string op_at_1st_booking_party { get; set; }

        /// <summary>OP AT 2ND Booking Party：XV_Name = 'OP AT 2ND BOOKING PARTY'</summary>
        public string op_at_2nd_booking_party { get; set; }

        /// <summary>DOC：XV_Name = 'DOC'</summary>
        public string doc { get; set; }

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
    /// 装箱明细（JobPackLines，货物信息）+ 该行对应的集装箱（JobContainer）
    /// 经中间表 JobContainerPackPivot 平铺：每行 = 一条货物明细携带其所属集装箱
    /// （J6_JL = JL_PK，J6_JC = JC_PK）。
    /// 例：一个集装箱含两条货物明细 => 返回两行，两行的 container 相同。
    /// </summary>
    public class ShipmentContainerOutput : JobPackLinesDtoOutput
    {
        public ShipmentContainerInfoOutput container { get; set; }
    }

    /// <summary>
    /// 集装箱 + 箱级汇总。JobContainer 上的 jc_f3_nkpacktype、jc_grossvolume、jc_description
    /// 实测在这条链路上都是空的，件数更是压根没有这一列 —— 箱级的件数/体积/毛重要经
    /// 中间表 JobContainerPackPivot 从 JobPackLines 汇总（本行的 jl_packagecount 只是
    /// 该箱下其中一条明细的件数，不等于整箱件数）。
    /// </summary>
    public class ShipmentContainerInfoOutput : JobContainerDtoOutput
    {
        /// <summary>箱型代码 jc_rc → RefContainer.RC_Code（前端不要直接显示 jc_rc 这个 pk）</summary>
        public string container_type_code { get; set; }

        /// <summary>箱型描述 RefContainer.RC_Description</summary>
        public string container_type_desc { get; set; }

        /// <summary>整箱件数 No. of Package：该箱下所有装箱明细 SUM(jl_packagecount)</summary>
        public int? pack_count { get; set; }

        /// <summary>整箱包装类型：该箱下装箱明细的 jl_f3_nkpacktype</summary>
        public string pack_type { get; set; }

        /// <summary>整箱体积：该箱下所有装箱明细 SUM(jl_actualvolume)</summary>
        public decimal? total_volume { get; set; }

        /// <summary>整箱毛重（按明细汇总）：SUM(jl_actualweight)。整箱申报毛重另见 jc_grossweight。</summary>
        public decimal? total_weight { get; set; }
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
