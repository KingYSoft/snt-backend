namespace SntBackend.Application.Billing.Dto.MatchTransaction
{
    public class WriteOffBankInput
    {
        /// <summary>
        /// receipt / payment
        /// </summary>
        public string Mode { get; set; }

        /// <summary>
        /// 结算公司名称
        /// </summary>
        public string SettleCompanyName { get; set; }

        /// <summary>
        /// 结算公司代码
        /// </summary>
        public string SettleCompanyCode { get; set; }
    }
}
