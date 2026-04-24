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
        public string ah_pk { get; set; }
        public string ah_transactionnum { get; set; }
        public string CompanyName { get; set; }
        public decimal ah_invoiceamount { get; set; }
        public string ah_rx_nktransactioncurrency { get; set; }
        public DateTime? ah_fullypaiddate { get; set; }
        public string ah_matchstatus { get; set; }
        public DateTime? ah_systemcreatetimeutc { get; set; }
        public string ah_desc { get; set; }
        public string ah_transactiontype { get; set; }
        public string ah_ledger { get; set; }
    }
}
