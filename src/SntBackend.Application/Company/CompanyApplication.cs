using Dapper;
using Facade;
using SntBackend.Application.Authorization;
using SntBackend.Application.Company.Dto;
using SntBackend.Application.SystemSettings;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.App;
using Abp.Application.Services.Dto;
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
        private readonly SystemAuditContextProvider _auditContextProvider;

        public CompanyApplication(
            IAppSqlServerRepository appSqlServerRepository,
            SystemAuditContextProvider auditContextProvider)
        {
            _appSqlServerRepository = appSqlServerRepository;
            _auditContextProvider = auditContextProvider;
        }

        public async Task<PagedResultDto<CompanyDtoOutput>> QueryPage(CompanyQueryInput input)
        {
            input ??= new CompanyQueryInput();
            var parameters = new DynamicParameters();
            var conditions = new List<string>
            {
                "gc_isactive = 1",
                "gc_isvalid = 1"
            };

            if (!string.IsNullOrWhiteSpace(input.query))
            {
                parameters.Add("search", $"%{input.query.Trim()}%");
                conditions.Add("(gc_code LIKE @search OR gc_name LIKE @search)");
            }

            var filterMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = "gc_code",
                ["name"] = "gc_name",
                ["business_reg_no"] = "gc_businessregno",
                ["business_reg_no2"] = "gc_businessregno2",
                ["customs_registration_no"] = "gc_customsregistrationno",
                ["address1"] = "gc_address1",
                ["address2"] = "gc_address2",
                ["city"] = "gc_city",
                ["state"] = "gc_state",
                ["postcode"] = "gc_postcode",
                ["phone"] = "gc_phone",
                ["fax"] = "gc_fax",
                ["email"] = "gc_email",
                ["web_address"] = "gc_webaddress",
                ["home_currency"] = "gc_rx_nklocalcurrency",
                ["country_code"] = "gc_rn_nkcountrycode"
            };
            var filterIndex = 0;
            foreach (var filter in input.filters ?? new List<SystemFilterItem>())
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.val))
                    continue;

                if (!filterMap.TryGetValue(filter?.key ?? string.Empty, out var column))
                    throw new AppException($"Unsupported company filter: {filter?.key}");

                var condition = SystemSettingsQueryHelper.BuildTextFilter(
                    column, filter, parameters, $"filter_{filterIndex++}");
                if (!string.IsNullOrWhiteSpace(condition))
                    conditions.Add(condition);
            }

            var where = "WHERE " + string.Join(" AND ", conditions);
            var skip = SystemSettingsQueryHelper.NormalizeSkip(input.SkipCount);
            var limit = SystemSettingsQueryHelper.NormalizeLimit(input.MaxResultCount);
            parameters.Add("skip", skip);
            parameters.Add("limit", limit);

            var total = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(
                $"SELECT COUNT(1) FROM GlbCompany {where}", parameters);
            var items = await _appSqlServerRepository.QueryAsync<CompanyDtoOutput>(@$"
SELECT
    gc_pk AS pk,
    gc_code AS code,
    gc_name AS name,
    gc_businessregno AS business_reg_no,
    gc_businessregno2 AS business_reg_no2,
    gc_customsregistrationno AS customs_registration_no,
    gc_address1 AS address1,
    gc_address2 AS address2,
    gc_city AS city,
    gc_phone AS phone,
    gc_postcode AS postcode,
    gc_state AS state,
    gc_fax AS fax,
    gc_email AS email,
    gc_webaddress AS web_address,
    gc_rx_nklocalcurrency AS home_currency,
    gc_rn_nkcountrycode AS country_code,
    gc_isactive AS is_active,
    gc_isvalid AS is_valid
FROM GlbCompany
{where}
ORDER BY gc_code, gc_pk
OFFSET @skip ROWS FETCH NEXT @limit ROWS ONLY", parameters);

            return new PagedResultDto<CompanyDtoOutput>
            {
                TotalCount = total,
                Items = items.ToList()
            };
        }

        public async Task<CompanyDtoOutput> Save(CompanySaveInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.code) || string.IsNullOrWhiteSpace(input.name))
                throw new AppException("Company code and name are required.");

            var duplicate = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 gc_pk
FROM GlbCompany
WHERE gc_code = @code AND (@pk IS NULL OR gc_pk <> @pk)",
                new { code = input.code.Trim(), pk = string.IsNullOrWhiteSpace(input.pk) ? null : input.pk.Trim() });
            if (!string.IsNullOrWhiteSpace(duplicate))
                throw new AppException("Company code already exists.");

            var pk = string.IsNullOrWhiteSpace(input.pk) ? SystemSettingsQueryHelper.NewPk() : input.pk.Trim();
            var audit = await _auditContextProvider.GetAsync();
            var parameters = new DynamicParameters();
            parameters.Add("pk", pk);
            parameters.Add("code", input.code.Trim());
            parameters.Add("name", input.name.Trim());
            parameters.Add("business_reg_no", input.business_reg_no);
            parameters.Add("business_reg_no2", input.business_reg_no2);
            parameters.Add("customs_registration_no", input.customs_registration_no);
            parameters.Add("address1", input.address1);
            parameters.Add("address2", input.address2);
            parameters.Add("city", input.city);
            parameters.Add("phone", input.phone);
            parameters.Add("postcode", input.postcode);
            parameters.Add("state", input.state);
            parameters.Add("fax", input.fax);
            parameters.Add("email", input.email);
            parameters.Add("web_address", input.web_address);
            parameters.Add("home_currency", input.home_currency);
            parameters.Add("country_code", input.country_code);
            parameters.Add("is_active", input.is_active == 0 ? 0 : 1);
            parameters.Add("is_valid", input.is_valid == 0 ? 0 : 1);
            parameters.Add("system_user", audit.UserCode);

            if (string.IsNullOrWhiteSpace(input.pk))
            {
                await _appSqlServerRepository.ExecuteAsync(@"
INSERT INTO GlbCompany
    (gc_pk, gc_code, gc_name, gc_businessregno, gc_businessregno2,
     gc_customsregistrationno, gc_address1, gc_address2, gc_city, gc_phone,
     gc_postcode, gc_state, gc_fax, gc_email, gc_webaddress,
     gc_isgstregistered, gc_isgstcashbasis, gc_iswhtregistered,
     gc_iswhtcashbasis, gc_noofaccountingperiods, gc_rx_nklocalcurrency,
     gc_rn_nkcountrycode, gc_isreciprocal, gc_localdoclanguage, gc_addressmap,
     gc_validationstatus, gc_autoversion, gc_systemcreateuser,
     gc_systemlastedituser, gc_isactive, gc_isvalid)
VALUES
    (@pk, @code, @name, @business_reg_no, @business_reg_no2,
     @customs_registration_no, @address1, @address2, @city, @phone,
     @postcode, @state, @fax, @email, @web_address,
     0, 0, 0, 0, 12, @home_currency, @country_code, 0, 'DEF', '',
     'MAN', 1, @system_user, @system_user, @is_active, @is_valid)", parameters);
            }
            else
            {
                var affected = await _appSqlServerRepository.ExecuteAsync(@"
UPDATE GlbCompany
SET gc_code = @code,
    gc_name = @name,
    gc_businessregno = @business_reg_no,
    gc_businessregno2 = @business_reg_no2,
    gc_customsregistrationno = @customs_registration_no,
    gc_address1 = @address1,
    gc_address2 = @address2,
    gc_city = @city,
    gc_phone = @phone,
    gc_postcode = @postcode,
    gc_state = @state,
    gc_fax = @fax,
    gc_email = @email,
    gc_webaddress = @web_address,
    gc_rx_nklocalcurrency = @home_currency,
    gc_rn_nkcountrycode = @country_code,
    gc_isactive = @is_active,
    gc_isvalid = @is_valid
WHERE gc_pk = @pk", parameters);
                if (affected == 0)
                    throw new AppException("Company not found.");
            }

            return await GetByPk(pk);
        }

        public async Task Delete(string pk)
        {
            if (string.IsNullOrWhiteSpace(pk))
                throw new AppException("Company primary key is required.");

            var branchCount = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(@"
SELECT COUNT(1)
FROM GlbBranch
WHERE gb_gc = @pk AND gb_isactive = 1 AND gb_isvalid = 1", new { pk });
            if (branchCount > 0)
                throw new AppException("Company is referenced by active branches.");

            var affected = await _appSqlServerRepository.ExecuteAsync(
                "UPDATE GlbCompany SET gc_isactive = 0 WHERE gc_pk = @pk", new { pk });
            if (affected == 0)
                throw new AppException("Company not found.");
        }

        private Task<CompanyDtoOutput> GetByPk(string pk)
        {
            return _appSqlServerRepository.QueryFirstOrDefaultAsync<CompanyDtoOutput>(@"
SELECT
    gc_pk AS pk, gc_code AS code, gc_name AS name,
    gc_businessregno AS business_reg_no, gc_businessregno2 AS business_reg_no2,
    gc_customsregistrationno AS customs_registration_no, gc_address1 AS address1,
    gc_address2 AS address2, gc_city AS city, gc_phone AS phone,
    gc_postcode AS postcode, gc_state AS state, gc_fax AS fax,
    gc_email AS email, gc_webaddress AS web_address,
    gc_rx_nklocalcurrency AS home_currency, gc_rn_nkcountrycode AS country_code,
    gc_isactive AS is_active, gc_isvalid AS is_valid
FROM GlbCompany WHERE gc_pk = @pk", new { pk });
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
