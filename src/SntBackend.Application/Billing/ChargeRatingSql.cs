namespace SntBackend.Application.Billing
{
    /// <summary>
    /// 费用行的计价依据（JobPaymentBasis）公共 SQL 片段 —— 数量 / 单位 / 单价的唯一可用来源。
    ///
    /// 背景：前端反馈账单页的 Unit Price / Unit / Qty 三列取不到值。实测下来这三个值
    /// 既不在 JobCharge 也不在 JobConsolCost 上，而在计价依据表 JobPaymentBasis：
    ///   PBS_JR → JobCharge.jr_pk        （运单费用行，PBS_IsCost 区分成本/收入侧）
    ///   PBS_E6 → JobConsolCost.E6_PK    （合单成本行，见 ConsolCostQuery）
    ///   PBS_ChargeableAmount = 数量，PBS_ChargeableUnit = 单位（实测是箱型，如 40HC），
    ///   PBS_PerUnitRate      = 单价
    /// 实测校验：qty × unit_price = 原币金额（如 4 × 295 = 1180）。
    ///
    /// 注意原先代码里的兜底 <c>COALESCE(al_unitqty, jr_productquantity)</c> 其实是条死路：
    /// JobCharge.JR_ProductQuantity 全库 556347 行**全是 0**，AccTransactionLines.AL_UnitQty
    /// 也只有 22/2035421 行非零，所以那个表达式恒为 0/NULL。真正有数的只有 JobPaymentBasis。
    ///
    /// 覆盖率仍然有限：运单费用行 106589/556347（19%）挂了计价依据，
    /// 合单成本行 353/1348（26%），其余行这三列本来就没有值。
    /// </summary>
    public static class ChargeRatingSql
    {
        /// <summary>
        /// 挂在 JobCharge 上的计价依据。<paramref name="chargeAlias"/> 是 JobCharge 的表别名，
        /// <paramref name="isCost"/> true = AP（成本）侧，false = AR（收入）侧。
        ///
        /// 一条费用行同一侧可能有多条计价依据（实测 128553 条是 1 条，其余最多 6 条以上），
        /// 混装时各条的费率与单位不同 —— 这种情况下 unit / unit_price 置空，只给合计数量，
        /// 由前端按 rating_line_count &gt; 1 提示"多档费率"，而不是硬凑一个数出来。
        /// </summary>
        public static string ByCharge(string chargeAlias, bool isCost) => $@"
OUTER APPLY (
    SELECT SUM(pbs.PBS_ChargeableAmount) AS qty,
           CASE WHEN COUNT(DISTINCT pbs.PBS_ChargeableUnit) = 1
                THEN MIN(pbs.PBS_ChargeableUnit) END AS unit,
           CASE WHEN COUNT(DISTINCT pbs.PBS_PerUnitRate) = 1
                THEN MIN(pbs.PBS_PerUnitRate) END  AS unit_price,
           COUNT(*)                                AS rating_line_count
    FROM JobPaymentBasis pbs
    WHERE pbs.PBS_JR = {chargeAlias}.jr_pk
        AND pbs.PBS_IsCost = {(isCost ? 1 : 0)}
) pb";

        /// <summary>
        /// 数量 / 单价的投影列。优先用已开票行上的值（虽然实测基本为空，但一旦有就以发票为准），
        /// 其次用计价依据。<paramref name="lineAlias"/> 是 AccTransactionLines 的表别名。
        /// </summary>
        public static string Columns(string lineAlias) => $@"
    COALESCE(NULLIF({lineAlias}.al_unitqty, 0), pb.qty)          AS qty,
    COALESCE(NULLIF({lineAlias}.al_unitprice, 0), pb.unit_price) AS unit_price,
    pb.unit,
    pb.rating_line_count";
    }
}
