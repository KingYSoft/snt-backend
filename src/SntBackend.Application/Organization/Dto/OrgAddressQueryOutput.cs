using Abp.Application.Services.Dto;

namespace SntBackend.Application.Organization.Dto
{
    public class OrgAddressQueryOutput : PagedResultDto<OrgAddressQueryDto>
    {
    }

    /// <summary>
    /// 组织 + 主地址一行（每个组织取一条启用地址，适合下拉数据源）。
    /// </summary>
    public class OrgAddressQueryDto
    {
        public string oh_pk { get; set; }
        public string oh_code { get; set; }
        public string oh_fullname { get; set; }

        public string oa_pk { get; set; }
        public string oa_code { get; set; }
        public string oa_companynameoverride { get; set; }
        public string oa_address1 { get; set; }
        public string oa_address2 { get; set; }
        public string oa_city { get; set; }
        public string oa_state { get; set; }
        public string oa_postcode { get; set; }
        public string oa_rn_nkcountrycode { get; set; }
        public string oa_phone { get; set; }
        public string oa_email { get; set; }
    }
}
