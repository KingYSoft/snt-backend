using Dapper;
using SntBackend.Application.Company.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Company
{
    /// <summary>
    /// 汇率查询（移植自 first-cargo CompanyApplication.Get，按 snt 的 ZZRefExchangeRate 表重写）。
    ///
    /// 数据模型差异：first-cargo 的 exchange_rate 有 exrate_from / exrate_to 两个币种列；
    /// snt 的 ZZRefExchangeRate 只有一个外币列 re_rx_nkexcurrency + re_sellrate，
    /// 该汇率把 re_rx_nkexcurrency 换算为“公司本位币”。故：
    ///   exrate_from = re_rx_nkexcurrency，exrate_to = 公司本位币（gc_rx_nklocalcurrency）。
    /// </summary>
    public class CompanyApplication : SntBackendApplicationBase, ICompanyApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public CompanyApplication(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

        public async Task<List<CompanyQueryRateOutput>> Get(GetCompanyInput input)
        {
            if (input == null)
                throw new Exception("Input cannot be empty.");

            // 目标币种：优先用入参 InvoiceCurrency，否则取公司本位币。
            var exrateTo = !string.IsNullOrWhiteSpace(input.InvoiceCurrency)
                ? input.InvoiceCurrency.Trim().ToUpperInvariant()
                : (await GetHomeCurrency())?.Trim().ToUpperInvariant();

            var invoiceDateRaw = input.InvoiceDate?.Trim();
            if (string.IsNullOrWhiteSpace(invoiceDateRaw))
                throw new Exception("InvoiceDate cannot be empty.");

            if (!DateTime.TryParse(invoiceDateRaw, out var invoiceDate))
                throw new Exception("InvoiceDate is invalid.");

            // 只保留日期部分，相当于固定为当天。
            invoiceDate = invoiceDate.Date;

            var currencies = input.HomeCurrency?
                .Select(c => c?.Trim().ToUpperInvariant())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList() ?? new List<string>();

            var results = new List<CompanyQueryRateOutput>();
            if (!currencies.Any())
                return results;

            foreach (var exrateFrom in currencies)
            {
                // 同币种，汇率为 1。
                if (!string.IsNullOrWhiteSpace(exrateTo)
                    && string.Equals(exrateFrom, exrateTo, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new CompanyQueryRateOutput
                    {
                        exrate_from = exrateFrom,
                        exrate_to = exrateTo,
                        exrate_sell_rate = "1"
                    });
                    continue;
                }

                var dp = new DynamicParameters();
                dp.Add("invoice_date", invoiceDate);
                dp.Add("exrate_from", exrateFrom);

                var sql = @"
SELECT TOP 1
    e.re_sellrate
FROM [dbo].[ZZRefExchangeRate] e
WHERE
    CAST(e.re_startdate AS date) <= @invoice_date
    AND (e.re_expirydate IS NULL OR CAST(e.re_expirydate AS date) >= @invoice_date)
    AND LTRIM(RTRIM(e.re_rx_nkexcurrency)) = @exrate_from
ORDER BY
    e.re_startdate DESC;";

                var sellRate = await _appSqlServerRepository.QueryFirstOrDefaultAsync<decimal?>(sql, dp);

                if (sellRate == null)
                    throw new Exception($"ExchangeRateNotFound ({exrateFrom}).");

                results.Add(new CompanyQueryRateOutput
                {
                    exrate_from = exrateFrom,
                    exrate_to = exrateTo,
                    exrate_sell_rate = sellRate.Value.ToString(CultureInfo.InvariantCulture)
                });
            }

            return results;
        }

        /// <summary>
        /// 本位币：取第一家启用的 GlbCompany 的本位币（gc_rx_nklocalcurrency）。
        /// snt 登录无“用户→分公司”映射，故按公司维度返回。
        /// </summary>
        private async Task<string> GetHomeCurrency()
        {
            var sql = @"
SELECT TOP 1 gc.gc_rx_nklocalcurrency
FROM GlbCompany gc
WHERE gc.gc_isactive = 1
    AND gc.gc_isvalid = 1
    AND gc.gc_rx_nklocalcurrency IS NOT NULL
ORDER BY gc.gc_code";
            return await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(sql);
        }
    }
}
