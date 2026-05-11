using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class QueryOrgAddressOutput
    {
        public List<QueryOrgAddressDto> List { get; set; } = new List<QueryOrgAddressDto>();
    }

    public class QueryOrgAddressDto
    {
        public string AH_OH { get; set; }
        public string OH_FullName { get; set; }
        public string OH_Code { get; set; }
    }
}
