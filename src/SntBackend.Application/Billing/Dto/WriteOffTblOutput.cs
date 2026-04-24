using System;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class WriteOffTblOutput
    {
        public int TotalCount { get; set; }
        public List<WriteOffTblItem> Items { get; set; } = new();
    }

    public class WriteOffTblItem
    {
        // MatchLink 字段
        public string ap_pk { get; set; }
        public decimal ap_amount { get; set; }
        public DateTime ap_matchdate { get; set; }
        public DateTime? ap_systemcreatetimeutc { get; set; }
        public string ap_reason { get; set; }
        public string ap_ah { get; set; }

        // Header 关联字段
        public string ah_transactionnum { get; set; }
        public string ah_rx_nktransactioncurrency { get; set; }
        public string ah_matchstatus { get; set; }
        public string ah_transactiontype { get; set; }

        // OrgHeader 关联字段
        public string CompanyName { get; set; }
    }
}
