using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Shipment.Dto
{
    public class ShipmentDetailOutput : JobShipmentDtoOutput
    {
        public JobDocAddressDtoOutput shipper { get; set; }
        public JobDocAddressDtoOutput consignee { get; set; }
        public JobDocAddressDtoOutput notify_party { get; set; }
        public JobDocAddressDtoOutput pickup { get; set; }
        public JobDocAddressDtoOutput delivery { get; set; }

        public List<ShipmentDetailContainerDto> containers_list { get; set; } = new();
        public List<JobPackLinesDtoOutput> loose_list { get; set; } = new();
        public JobDocumentDataDtoOutput doc_data { get; set; }
    }

    public class ShipmentDetailContainerDto : JobContainerDtoOutput
    {
        public string jl_rh_nkcommoditycode { get; set; }
        public decimal? jl_actualweight { get; set; }
        public decimal? jl_actualvolume { get; set; }
        public int? jl_packagecount { get; set; }
        public string jl_f3_nkpacktype { get; set; }
        public string jl_description { get; set; }
    }
}
