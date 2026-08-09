namespace SntBackend.Application.Po.Dto
{
    /// <summary>
    /// 发票行的展示扩展列。AccTransactionLines 上 al_ac / al_at / al_gb / al_ge 存的都是 pk，
    /// 这里补出对应的代码与名称。仅
    /// <see cref="SntBackend.Application.Billing.BillingApplication.QueryChargesByInvoiceNo"/>
    /// 填充，其他查询为 null。
    ///
    /// 关于数量与单价：al_unitqty / al_unitprice 实测在 203 万行里分别只有 22 / 15 行非零，
    /// 也就是这两列在本库基本没被写入。发票编辑页的数量单价请取同一接口返回的 Charges
    /// （BillingChargeLineItem.qty / unit_price），那边在开票行为空时会回退到
    /// JobCharge.jr_productquantity 与 原币金额/数量。
    /// </summary>
    public partial class AccTransactionLinesDtoOutput
    {
        /// <summary>费用代码 al_ac → AccChargeCode.ac_code</summary>
        public string charge_code { get; set; }

        /// <summary>费用代码描述 AccChargeCode.ac_desc</summary>
        public string charge_desc { get; set; }

        /// <summary>税代码 al_at → AccTaxRate.AT_Code</summary>
        public string tax_code { get; set; }

        /// <summary>税代码描述 AccTaxRate.AT_Description</summary>
        public string tax_desc { get; set; }

        /// <summary>分公司代码 al_gb → GlbBranch.GB_Code</summary>
        public string branch_code { get; set; }

        /// <summary>分公司名称 GlbBranch.GB_BranchName</summary>
        public string branch_name { get; set; }

        /// <summary>部门代码 al_ge → GlbDepartment.GE_Code</summary>
        public string dept_code { get; set; }

        /// <summary>部门描述 GlbDepartment.GE_Desc</summary>
        public string dept_desc { get; set; }
    }
}
