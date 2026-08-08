namespace SntBackend.Application.Po.Dto
{
    public partial class AccTransactionHeaderDtoOutput
    {
        /// <summary>
        /// 结算公司名称（OrgHeader.OH_FullName）
        /// </summary>
        public string oh_fullname { get; set; }

        /// <summary>
        /// 是否草稿：ah_postdate 有值（已过账）= "N"，否则 "Y"。
        /// 口径与 <see cref="SntBackend.Application.Consolidation.ConsolCostQuery"/> 费用行上的同名字段一致。
        /// 仅合单侧查询填充，shipment 侧为 null。
        /// </summary>
        public string Draft { get; set; }

        /// <summary>
        /// 本合单分摊额：该合单下引用这张发票的 JobConsolCost 行的 E6_LocalCostAmount 之和。
        ///
        /// 一张供应商发票可以同时挂在多个合单下（实测存在一张发票跨 7 个合单的情况），
        /// 此时 <c>ah_invoiceamount</c> 是整张发票的金额，在每个合单下都相同，
        /// 而本字段只算归属当前合单的那部分。实测差距可以很大：某合单下
        /// ah_invoiceamount = -485040.5，而本字段只有 -208.5。
        ///
        /// 符号：取负，与 AP 在 AccTransactionHeader 里存负数的惯例一致，保证同一行上
        /// 两个金额同号可直接比较。因此与 GetBillingSummary 返回的正数 ap 符号相反，
        /// 但绝对值仍相等（同一合单下本字段之和 = -ap）。
        /// 仅合单侧查询填充，shipment 侧为 null。
        /// </summary>
        public decimal? apportioned_local_amount { get; set; }

        /// <summary>
        /// 发票金额（不含税，本位币）= ah_invoiceamount。
        /// 这几个别名的口径与验证见
        /// <see cref="SntBackend.Application.Billing.AccTransactionHeaderSql"/>。
        /// </summary>
        public decimal? amount_tax_excl { get; set; }

        /// <summary>税额（本位币）= ah_gstamount</summary>
        public decimal? tax_amount { get; set; }

        /// <summary>发票金额（含税，本位币）= ah_localtotal = amount_tax_excl + tax_amount</summary>
        public decimal? amount_tax_incl { get; set; }

        /// <summary>发票金额（含税，原币）= ah_ostotal，币种见 ah_rx_nktransactioncurrency</summary>
        public decimal? os_amount_tax_incl { get; set; }

        /// <summary>分公司代码 ah_gb → GlbBranch.GB_Code</summary>
        public string branch_code { get; set; }

        /// <summary>分公司名称 GlbBranch.GB_BranchName</summary>
        public string branch_name { get; set; }

        /// <summary>部门代码 ah_ge → GlbDepartment.GE_Code</summary>
        public string dept_code { get; set; }

        /// <summary>部门描述 GlbDepartment.GE_Desc</summary>
        public string dept_desc { get; set; }
    }
}
