using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Consolidation.Dto
{
    public class ConsolidationTblOutput
    {
        public int TotalCount { get; set; }
        public List<JobConsolDtoOutput> Items { get; set; } = new();
    }
}
