using System;
using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    public class BillingChargeLineOutput
    {
        public int TotalCount { get; set; }

        public List<BillingChargeLineItem> Items { get; set; } = new();
    }

    public class BillingChargeLineItem
    {
        public string jr_pk { get; set; }
        public string jr_jh { get; set; }
        public string jr_chargetype { get; set; }
        public string jr_desc { get; set; }
        public decimal? amount { get; set; }
        public decimal? os_amount { get; set; }
        public string currency { get; set; }
        public string party_oh { get; set; }
        public decimal? exchange_rate { get; set; }
        public string gst_rate { get; set; }
        public string wht_rate { get; set; }
        public string vat_class { get; set; }
        public string line_pk { get; set; }
        public string invoice_pk { get; set; }
        public string invoice_no { get; set; }
        public DateTime? invoice_date { get; set; }
        public string Draft { get; set; }
    }
}
