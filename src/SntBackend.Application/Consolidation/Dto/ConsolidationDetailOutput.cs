using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Consolidation.Dto
{
    public class ConsolidationAgentOutput : JobDocAddressDtoOutput
    {
        public OrgAddressDtoOutput org_address { get; set; }
    }

    public class ConsolidationDetailOutput : JobConsolDtoOutput
    {
        public ConsolidationAgentOutput local_agent { get; set; }
        public ConsolidationAgentOutput overseas_agent { get; set; }
        public List<JobConsolTransportDtoOutput> transport_list { get; set; } = new();
        public List<JobShipmentDtoOutput> shps { get; set; } = new();
        public List<JobContainerDtoOutput> containers { get; set; } = new();
    }
}
