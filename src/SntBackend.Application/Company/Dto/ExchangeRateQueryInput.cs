namespace SntBackend.Application.Company.Dto
{
    public class ExchangeRateQueryInput
    {
        public string currencyFrom { get; set; }
        public string currencyTo { get; set; }
        public string period { get; set; }
    }
}
