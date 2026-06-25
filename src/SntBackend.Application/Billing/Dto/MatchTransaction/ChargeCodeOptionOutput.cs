namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    /// <summary>
    /// 费用代码下拉项（来源 AccChargeCode）
    /// </summary>
    public class ChargeCodeOptionOutput
    {
        public string pk { get; set; }
        public string code { get; set; }
        public string desc { get; set; }
        /// <summary>费用类型（ac_chargetype）</summary>
        public string charge_type { get; set; }
    }
}
