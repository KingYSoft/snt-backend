namespace SntBackend.Application.Billing.Dto
{
    public class BillingSummaryDto
    {
        public decimal grossProfitMargin { get; set; }

        public decimal ar { get; set; }

        public decimal ap { get; set; }

        public decimal profits { get; set; }

        public string home_currency { get; set; }
    }
}
