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
        public decimal? qty { get; set; }
        public decimal? unit_price { get; set; }
        public string currency { get; set; }
        public string party_oh { get; set; }
        /// <summary>客户/供应商代码 OrgHeader.oh_code</summary>
        public string party_code { get; set; }
        /// <summary>客户/供应商名称 OrgHeader.oh_fullname</summary>
        public string party_name { get; set; }
        public decimal? exchange_rate { get; set; }
        public string gst_rate { get; set; }
        public string wht_rate { get; set; }
        public string vat_class { get; set; }
        /// <summary>发票类型 jr_invoicetype</summary>
        public string jr_invoicetype { get; set; }
        /// <summary>分公司/分支 jr_gb（GlbBranch.gb_pk）</summary>
        public string jr_gb { get; set; }
        /// <summary>分支代码 GlbBranch.gb_code</summary>
        public string branch_code { get; set; }
        /// <summary>分支名称 GlbBranch.gb_branchname</summary>
        public string branch_name { get; set; }
        public string line_pk { get; set; }
        public string invoice_pk { get; set; }
        public string invoice_no { get; set; }
        public DateTime? invoice_date { get; set; }
        public string Draft { get; set; }
    }
}
