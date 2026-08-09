namespace SntBackend.Application.Billing
{
    /// <summary>
    /// 发票头（AccTransactionHeader）展示列的公共 SQL 片段。
    ///
    /// 前端反馈发票列表的金额列对不上，原因是这几列的含税口径不能望文生义（实测校验）：
    ///   ah_invoiceamount = 本位币【不含税】金额
    ///   ah_gstamount     = 本位币税额
    ///   ah_localtotal    = 本位币【含税】总额  = ah_invoiceamount + ah_gstamount
    ///   ah_ostotal       = 原币【含税】总额
    /// 例：AR 2603002882 → 2668.57 + 231.43 = 2900.00 = ah_localtotal = ah_ostotal（汇率 1）；
    ///     AR 2503002601 → 5735.44 + 344.13 = 6079.57 = ah_localtotal，ah_ostotal = 848 USD @7.1693。
    /// 所以这里额外投影一组名字直白的别名，前端不用再猜哪列含税。
    ///
    /// 分公司 / 部门同理：ah_gb、ah_ge 存的是 pk，展示要 join 出代码和名称
    /// （AP 侧实测 95803 张发票两列全部有值）。
    ///
    /// 用法：SELECT x.*, {DisplayColumns("x")} ... FROM AccTransactionHeader x {DisplayJoins("x")}
    /// </summary>
    public static class AccTransactionHeaderSql
    {
        /// <summary>投影列，跟在 <c>{alias}.*</c> 后面。<paramref name="alias"/> 是 AccTransactionHeader 的表别名。</summary>
        public static string DisplayColumns(string alias) => $@"
    {alias}.ah_invoiceamount AS amount_tax_excl,
    {alias}.ah_gstamount     AS tax_amount,
    {alias}.ah_localtotal    AS amount_tax_incl,
    {alias}.ah_ostotal       AS os_amount_tax_incl,
    ah_gb_disp.GB_Code       AS branch_code,
    ah_gb_disp.GB_BranchName AS branch_name,
    ah_ge_disp.GE_Code       AS dept_code,
    ah_ge_disp.GE_Desc       AS dept_desc";

        /// <summary>上面那些列需要的 JOIN，跟在 <c>FROM AccTransactionHeader {alias}</c> 后面。</summary>
        public static string DisplayJoins(string alias) => $@"
LEFT JOIN GlbBranch     ah_gb_disp ON ah_gb_disp.GB_PK = {alias}.ah_gb
LEFT JOIN GlbDepartment ah_ge_disp ON ah_ge_disp.GE_PK = {alias}.ah_ge";
    }
}
