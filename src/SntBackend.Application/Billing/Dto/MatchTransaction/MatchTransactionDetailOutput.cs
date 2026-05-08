using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class MatchTransactionDetailOutput
    {
        public AccTransactionHeaderDtoOutput Header { get; set; }
        public List<AccTransactionLinesDtoOutput> Lines { get; set; } = new();
    }
}
