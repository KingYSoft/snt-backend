using SntBackend.Application.Po.Dto;
using System;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class WriteOffDetailOutput
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
        public decimal ah_outstandingamount { get; set; }
        public decimal ah_ostotal { get; set; }
        public List<AccTransactionMatchLinkDtoOutput> MatchLinks { get; set; } = new();
    }
}
