using SntBackend.Application.Po.Dto;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class MatchTransactionDetailOutput
    {
        public AccTransactionHeaderDtoOutput Header { get; set; }
        public AccBankAccountDtoOutput Bank { get; set; }
        /// <summary>
        /// 当前结算单所属匹配组的全部匹配记录。金额来自
        /// AccTransactionMatchLink，而不是 REC/PAY 结算头的 ah_ostotal。
        /// </summary>
        public List<AccTransactionMatchLinkDtoOutput> MatchLinks { get; set; } = new();
        public List<OutstandingInvoiceItem> Lines { get; set; } = new();
    }
}
