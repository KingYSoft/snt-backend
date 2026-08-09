using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Consolidation.Dto
{
    /// <summary>
    /// OrgAddress 扩展 DTO（包含 OrgHeader 信息）
    /// </summary>
    public class ConsolidationOrgAddressOutput : OrgAddressDtoOutput
    {
        public string oh_fullname { get; set; }
    }

    public class ConsolidationAgentOutput : JobDocAddressDtoOutput
    {
        public ConsolidationOrgAddressOutput org_address { get; set; }

        /// <summary>
        /// 代理名称。实测 JobDocAddress.e2_companyname 在 CEC/CIC 上几乎全空
        /// （13300 条里 13299 条为空），名称只能从 e2_oa_address 关联到
        /// OrgAddress → OrgHeader.oh_fullname 拿。这里把它拍平成一个字段，
        /// 免得前端还要下钻 org_address。
        /// </summary>
        public string name { get; set; }

        /// <summary>代理地址代码 OrgAddress.oa_code</summary>
        public string code { get; set; }
    }

    /// <summary>
    /// 路由（运输航段）+ 该航段的承运人与所属合单号。
    /// jw_parentguid 只是合单 pk，展示要的是合单号，这里一并反查出来。
    /// </summary>
    public class ConsolidationTransportOutput : JobConsolTransportDtoOutput
    {
        /// <summary>所属合单号 JobConsol.jk_uniqueconsignref（由 jw_parentguid 反查）</summary>
        public string consol_no { get; set; }

        /// <summary>承运人名称 jw_oa_carrieraddress → OrgAddress → OrgHeader.oh_fullname</summary>
        public string carrier_name { get; set; }

        /// <summary>承运人代码 OrgHeader.oh_code</summary>
        public string carrier_code { get; set; }
    }

    /// <summary>
    /// 合单下的运单列表行。JobShipment.* 之外补上展示要用、但需要关联才能拿到的列。
    /// </summary>
    public class ConsolidationShipmentOutput : JobShipmentDtoOutput
    {
        /// <summary>发货人 JobDocAddress('JS','CRD') → OrgHeader.oh_fullname</summary>
        public string shipper_name { get; set; }

        /// <summary>收货人 JobDocAddress('JS','CEG') → OrgHeader.oh_fullname</summary>
        public string consignee_name { get; set; }

        /// <summary>
        /// 承运人。优先取运单自己的 js_oa_bookedshippinglineaddress；
        /// 实测运单侧只有 47% 有值（合单下的运单常常整片为空），为空时回落到
        /// 本合单主程航段（jw_transporttype = 'MAI'）的承运人。
        /// </summary>
        public string carrier_name { get; set; }

        /// <summary>
        /// 体积 CBM = js_actualvolume。单位见 js_unitofvolume。
        /// </summary>
        public decimal? cbm { get; set; }

        /// <summary>
        /// 件数 = js_outerpacks。
        /// 注意不是 js_totalpackagecount —— 实测后者 44803 条里只有 29 条非零，基本恒为 0。
        /// </summary>
        public int? packages { get; set; }

        /// <summary>包装类型 = js_f3_nkpacktype（与 packages 取的 js_outerpacks 配套）</summary>
        public string pack_type { get; set; }

        /// <summary>货物描述 = js_goodsdescription</summary>
        public string goods_description { get; set; }
    }

    /// <summary>
    /// 合单下的集装箱行。JobContainer 本身没有件数/体积/品名的有效值
    /// （实测 jc_grossvolume、jc_description、jc_f3_nkpacktype 在合单箱上都是空的），
    /// 这些要经中间表 JobContainerPackPivot 从 JobPackLines 汇总。
    /// </summary>
    public class ConsolidationContainerOutput : JobContainerDtoOutput
    {
        /// <summary>箱型代码 jc_rc → RefContainer.RC_Code（前端不要直接显示 jc_rc 这个 pk）</summary>
        public string container_type_code { get; set; }

        /// <summary>箱型描述 RefContainer.RC_Description</summary>
        public string container_type_desc { get; set; }

        /// <summary>件数：该箱下所有装箱明细 SUM(jl_packagecount)</summary>
        public int? pack_count { get; set; }

        /// <summary>包装类型：该箱下装箱明细的 jl_f3_nkpacktype</summary>
        public string pack_type { get; set; }

        /// <summary>体积 CBM：该箱下所有装箱明细 SUM(jl_actualvolume)</summary>
        public decimal? total_volume { get; set; }

        /// <summary>毛重：该箱下所有装箱明细 SUM(jl_actualweight)。整箱毛重另见 jc_grossweight。</summary>
        public decimal? total_weight { get; set; }

        /// <summary>货物描述：取该箱下第一条非空的 jl_description</summary>
        public string goods_description { get; set; }
    }

    public class ConsolidationDetailOutput : JobConsolDtoOutput
    {
        public ConsolidationAgentOutput local_agent { get; set; }
        public ConsolidationAgentOutput overseas_agent { get; set; }
        public List<ConsolidationTransportOutput> transport_list { get; set; } = new();
        public List<ConsolidationShipmentOutput> shps { get; set; } = new();
        public List<ConsolidationContainerOutput> containers { get; set; } = new();
    }
}
