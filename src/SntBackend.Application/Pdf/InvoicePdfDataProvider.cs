using Abp.Dependency;
using Dapper;
using SntBackend.Application.Pdf.Dto;
using SntBackend.DomainService.Share.App;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Pdf
{
    /// <summary>
    /// 按发票号从 snt 库装配发票 PDF 渲染模型。
    /// 数据源：AccTransactionHeader / AccTransactionLines（发票头行）、JobHeader → JobShipment（业务信息）、
    /// JobConsol + JobConsolTransport（船名航次 / 主单号）、JobDocAddress（发货人 / 收货人）、
    /// GlbBranch + GlbCompany（抬头）、AccBankAccount（收款银行）。
    /// </summary>
    public class InvoicePdfDataProvider : ITransientDependency
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        /// <summary>
        /// 初始化 <see cref="InvoicePdfDataProvider"/> 的新实例。
        /// </summary>
        public InvoicePdfDataProvider(IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

        /// <summary>
        /// 装配一张发票的渲染模型；发票不存在时返回 null。
        /// </summary>
        /// <param name="invoiceNo">发票号，先按 ah_transactionnum 匹配，再回落 ah_consolidatedinvoiceref。</param>
        /// <param name="ledgerType">账本类型 AR / AP，留空不过滤。</param>
        public async Task<InvoicePdfTemplateDto> BuildAsync(string invoiceNo, string ledgerType)
        {
            var head = await QueryHeadAsync(invoiceNo, ledgerType);
            if (head == null)
            {
                return null;
            }

            var dp = new DynamicParameters();
            dp.Add("ahPk", head.ah_pk);
            dp.Add("ohPk", head.ah_oh);
            dp.Add("oaOverride", head.ah_oa_invoiceaddressoverride);
            dp.Add("gbPk", head.ah_gb);
            dp.Add("gcPk", head.ah_gc);
            dp.Add("jhPk", head.ah_jh);
            dp.Add("abPk", head.ah_ab);
            dp.Add("ccy", head.ah_rx_nktransactioncurrency);
            dp.Add("createUser", head.ah_systemcreateuser);

            var sql = @"
-- 1) 发票行 + 费用代码
SELECT
    al.AL_Sequence                  AS seq,
    al.AL_Desc                      AS line_desc,
    al.AL_UnitQty                   AS qty,
    al.AL_OSUnitPrice               AS unit_price,
    al.AL_OSAmount                  AS amount,
    al.AL_LineAmount                AS amount_local,
    al.AL_RX_NKTransactionCurrency  AS currency,
    al.AL_ExchangeRate              AS exchange_rate,
    al.AL_GSTVAT                    AS gst,
    ac.AC_Code                      AS charge_code,
    ac.AC_Desc                      AS charge_desc,
    ac.AC_LocalLanguageDescription  AS charge_desc_local
FROM AccTransactionLines al
LEFT JOIN AccChargeCode ac ON ac.AC_PK = al.AL_AC
WHERE al.AL_AH = @ahPk
ORDER BY al.AL_Sequence;

-- 2) 账单方（发票地址优先取 ah_oa_invoiceaddressoverride，否则取该组织第一条启用地址）
SELECT TOP 1
    oh.OH_Code      AS code,
    oh.OH_FullName  AS name,
    oa.OA_Address1  AS addr1,
    oa.OA_Address2  AS addr2,
    oa.OA_City      AS city,
    oa.OA_State     AS state,
    oa.OA_PostCode  AS postcode,
    oa.OA_RN_NKCountryCode AS country
FROM OrgHeader oh
OUTER APPLY (
    SELECT TOP 1 a.OA_Address1, a.OA_Address2, a.OA_City, a.OA_State, a.OA_PostCode, a.OA_RN_NKCountryCode
    FROM OrgAddress a
    WHERE a.OA_PK = @oaOverride
       OR (@oaOverride IS NULL AND a.OA_OH = oh.OH_PK AND a.OA_IsActive = 1)
    ORDER BY CASE WHEN a.OA_PK = @oaOverride THEN 0 ELSE 1 END, a.OA_Code
) oa
WHERE oh.OH_PK = @ohPk;

-- 3) 抬头：分公司为主，公司兜底
SELECT TOP 1
    gb.GB_BranchName    AS branch_name,
    gb.GB_Address1      AS branch_addr1,
    gb.GB_Address2      AS branch_addr2,
    gb.GB_City          AS branch_city,
    gb.GB_State         AS branch_state,
    gb.GB_PostCode      AS branch_postcode,
    gb.GB_Phone         AS branch_phone,
    gc.GC_Name          AS company_name,
    gc.GC_Address1      AS company_addr1,
    gc.GC_Address2      AS company_addr2,
    gc.GC_City          AS company_city,
    gc.GC_State         AS company_state,
    gc.GC_PostCode      AS company_postcode,
    gc.GC_Phone         AS company_phone,
    gc.GC_RX_NKLocalCurrency AS home_currency
FROM (SELECT 1 AS x) t
LEFT JOIN GlbBranch  gb ON gb.GB_PK = @gbPk
LEFT JOIN GlbCompany gc ON gc.GC_PK = COALESCE(@gcPk, gb.GB_GC);

-- 4) 业务单 + 运单
SELECT TOP 1
    jh.JH_JobNum        AS job_num,
    jh.JH_TransportMode AS job_transport_mode,
    js.JS_HouseBill     AS hbl,
    js.JS_TransportMode AS transport_mode,
    js.JS_RL_NKOrigin       AS origin,
    js.JS_RL_NKDestination  AS destination,
    js.JS_ActualWeight  AS weight,
    js.JS_UnitOfWeight  AS weight_unit,
    js.JS_ActualVolume  AS volume,
    js.JS_UnitOfVolume  AS volume_unit,
    js.JS_ActualChargeable AS chargeable,
    js.JS_OuterPacks    AS packs
FROM JobHeader jh
LEFT JOIN JobShipment js ON js.JS_PK = jh.JH_ParentID AND jh.JH_ParentTableCode = 'JS'
WHERE jh.JH_PK = @jhPk;

-- 5) 拼箱 + 第一程运输（船名航次 / ETD / ETA / 主单号）
SELECT TOP 1
    c.JK_UniqueConsignRef AS consol_no,
    c.JK_MasterBillNum    AS mbl,
    t.JW_Vessel           AS vessel,
    t.JW_VoyageFlight     AS voyage,
    t.JW_ETD              AS etd,
    t.JW_ETA              AS eta
FROM JobHeader jh
INNER JOIN JobConShipLink l ON l.JN_JS = jh.JH_ParentID
INNER JOIN JobConsol c      ON c.JK_PK = l.JN_JK
LEFT  JOIN JobConsolTransport t ON t.JW_ParentGUID = c.JK_PK AND t.JW_IsValid = 1
WHERE jh.JH_PK = @jhPk
    AND jh.JH_ParentTableCode = 'JS'
ORDER BY t.JW_LegOrder;

-- 6) 发货人 CRD / 收货人 CEG
SELECT
    jda.E2_AddressType AS addr_type,
    COALESCE(NULLIF(jda.E2_CompanyName, ''), NULLIF(oa.OA_CompanyNameOverride, ''), oh.OH_FullName) AS name
FROM JobHeader jh
INNER JOIN JobDocAddress jda ON jda.E2_ParentID = jh.JH_ParentID AND jda.E2_ParentTableCode = 'JS'
LEFT JOIN OrgAddress oa ON oa.OA_PK = jda.E2_OA_Address
LEFT JOIN OrgHeader  oh ON oh.OH_PK = oa.OA_OH
WHERE jh.JH_PK = @jhPk
    AND jda.E2_AddressType IN ('CRD', 'CEG');

-- 7) 集装箱
SELECT DISTINCT
    jc.JC_ContainerNum AS container_no,
    rc.RC_Code         AS container_type
FROM JobHeader jh
INNER JOIN JobPackLines jl ON jl.JL_JS = jh.JH_ParentID
INNER JOIN JobContainerPackPivot p ON p.J6_JL = jl.JL_PK
INNER JOIN JobContainer jc ON jc.JC_PK = p.J6_JC
LEFT  JOIN RefContainer rc ON rc.RC_PK = jc.JC_RC
WHERE jh.JH_PK = @jhPk;

-- 8) 收款银行：发票指定 > 分公司+币种 > 公司+币种
--    账号取 AB_FullAccountNumber / AB_AccountNum（AB_AccountNumber 在本库全为空）
SELECT TOP 1 bank.*
FROM (
    SELECT 0 AS priority, ab.AB_BankAccountName AS beneficiary, ab.AB_BankName AS bank_name,
           ab.AB_BankAddress AS bank_address, ab.AB_SWIFT AS swift,
           COALESCE(NULLIF(ab.AB_FullAccountNumber, ''), NULLIF(ab.AB_AccountNum, ''), ab.AB_AccountNumber) AS account_no
    FROM AccBankAccount ab
    WHERE ab.AB_PK = @abPk
    UNION ALL
    SELECT 1, ab.AB_BankAccountName, ab.AB_BankName, ab.AB_BankAddress, ab.AB_SWIFT,
           COALESCE(NULLIF(ab.AB_FullAccountNumber, ''), NULLIF(ab.AB_AccountNum, ''), ab.AB_AccountNumber)
    FROM AccBankAccount ab
    WHERE ab.AB_IsActive = 1
        AND ab.AB_GB = @gbPk
        AND ab.AB_RX_NKAccountCurrency = @ccy
        AND (ISNULL(ab.AB_BankName, '') <> '' OR ISNULL(ab.AB_SWIFT, '') <> '')
    UNION ALL
    SELECT 2, ab.AB_BankAccountName, ab.AB_BankName, ab.AB_BankAddress, ab.AB_SWIFT,
           COALESCE(NULLIF(ab.AB_FullAccountNumber, ''), NULLIF(ab.AB_AccountNum, ''), ab.AB_AccountNumber)
    FROM AccBankAccount ab
    WHERE ab.AB_IsActive = 1
        AND ab.AB_GC = @gcPk
        AND ab.AB_RX_NKAccountCurrency = @ccy
        AND (ISNULL(ab.AB_BankName, '') <> '' OR ISNULL(ab.AB_SWIFT, '') <> '')
) bank
ORDER BY bank.priority;

-- 9) 签发人
SELECT TOP 1 gs.GS_FullName AS full_name
FROM GlbStaff gs
WHERE gs.GS_Code = @createUser;
";

            using var multi = await _appSqlServerRepository.QueryMultipleAsync(sql, dp);

            var lines = (await multi.ReadAsync<LineRow>()).ToList();
            var party = await multi.ReadFirstOrDefaultAsync<PartyRow>();
            var letterhead = await multi.ReadFirstOrDefaultAsync<LetterheadRow>();
            var job = await multi.ReadFirstOrDefaultAsync<JobRow>();
            var consol = await multi.ReadFirstOrDefaultAsync<ConsolRow>();
            var addresses = (await multi.ReadAsync<DocAddressRow>()).ToList();
            var containers = (await multi.ReadAsync<ContainerRow>()).ToList();
            var bank = await multi.ReadFirstOrDefaultAsync<BankRow>();
            var issuer = await multi.ReadFirstOrDefaultAsync<string>();

            return Compose(head, lines, party, letterhead, job, consol, addresses, containers, bank, issuer);
        }

        /// <summary>
        /// 按发票号定位发票头。
        /// </summary>
        private async Task<HeadRow> QueryHeadAsync(string invoiceNo, string ledgerType)
        {
            var dp = new DynamicParameters();
            dp.Add("invoiceNo", invoiceNo);
            dp.Add("ledger", string.IsNullOrWhiteSpace(ledgerType) ? null : ledgerType.Trim().ToUpperInvariant());

            var sql = @"
SELECT TOP 1
    ah.AH_TransactionNum            AS ah_transactionnum,
    ah.AH_ConsolidatedInvoiceRef    AS ah_consolidatedinvoiceref,
    ah.AH_Ledger                    AS ah_ledger,
    ah.AH_InvoiceDate               AS ah_invoicedate,
    ah.AH_DueDate                   AS ah_duedate,
    ah.AH_PostDate                  AS ah_postdate,
    ah.AH_GSTAmount                 AS ah_gstamount,
    ah.AH_OSTotal                   AS ah_ostotal,
    ah.AH_LocalTotal                AS ah_localtotal,
    ah.AH_RX_NKTransactionCurrency  AS ah_rx_nktransactioncurrency,
    ah.AH_ExchangeRate              AS ah_exchangerate,
    ah.AH_InvoiceTerm               AS ah_invoiceterm,
    ah.AH_InvoiceTermDays           AS ah_invoicetermdays,
    ah.AH_JobNumber                 AS ah_jobnumber,
    CAST(ah.AH_PK AS varchar(36))   AS ah_pk,
    CAST(ah.AH_OH AS varchar(36))   AS ah_oh,
    -- 发票头的 ah_jh 约 20% 为空（多为 AP），此时回落到发票行的 al_jh，
    -- 否则整块运输信息（船名/提单号/收发货人/重量体积）全空
    COALESCE(
        CAST(ah.AH_JH AS varchar(36)),
        (SELECT TOP 1 CAST(al.AL_JH AS varchar(36))
         FROM AccTransactionLines al
         WHERE al.AL_AH = ah.AH_PK AND al.AL_JH IS NOT NULL
         ORDER BY al.AL_Sequence)
    )                               AS ah_jh,
    CAST(ah.AH_GB AS varchar(36))   AS ah_gb,
    CAST(ah.AH_GC AS varchar(36))   AS ah_gc,
    CAST(ah.AH_AB AS varchar(36))   AS ah_ab,
    CAST(ah.AH_OA_InvoiceAddressOverride AS varchar(36)) AS ah_oa_invoiceaddressoverride,
    ah.AH_SystemCreateUser          AS ah_systemcreateuser,
    ah.AH_SystemCreateTimeUtc       AS ah_systemcreatetimeutc
FROM AccTransactionHeader ah
WHERE ah.AH_IsCancelled = 0
    AND (ah.AH_TransactionNum = @invoiceNo OR ah.AH_ConsolidatedInvoiceRef = @invoiceNo)
    AND (@ledger IS NULL OR ah.AH_Ledger = @ledger)
ORDER BY CASE WHEN ah.AH_TransactionNum = @invoiceNo THEN 0 ELSE 1 END,
    ah.AH_InvoiceDate DESC, ah.AH_PK DESC
";
            return await _appSqlServerRepository.QueryFirstOrDefaultAsync<HeadRow>(sql, dp);
        }

        /// <summary>
        /// 把查询结果拼装成渲染模型。
        /// </summary>
        private static InvoicePdfTemplateDto Compose(
            HeadRow head,
            List<LineRow> lines,
            PartyRow party,
            LetterheadRow letterhead,
            JobRow job,
            ConsolRow consol,
            List<DocAddressRow> addresses,
            List<ContainerRow> containers,
            BankRow bank,
            string issuer)
        {
            var isAp = string.Equals(head.ah_ledger, "AP", StringComparison.OrdinalIgnoreCase);
            var currency = Trim(head.ah_rx_nktransactioncurrency);
            var rate = head.ah_exchangerate.GetValueOrDefault();

            // 金额口径（已按库中数据验证）：
            //   AL_OSAmount / AH_OSTotal   = 交易币含税金额
            //   AL_LineAmount / AH_LocalTotal = 本位币金额（行级为不含税净额）
            //   AH_GSTAmount / AL_GSTVAT   = 本位币税额
            var total = head.ah_ostotal.GetValueOrDefault();
            if (total == 0)
            {
                total = lines.Sum(x => x.amount.GetValueOrDefault());
            }

            var taxLocal = head.ah_gstamount.GetValueOrDefault();
            if (taxLocal == 0)
            {
                taxLocal = lines.Sum(x => x.gst.GetValueOrDefault());
            }

            // 税额换算回交易币：本位币 = 交易币 × 汇率
            var tax = rate > 0 ? decimal.Round(taxLocal / rate, 2, MidpointRounding.AwayFromZero) : taxLocal;
            var subTotal = total - tax;

            var transportMode = Trim(job?.transport_mode) ?? Trim(job?.job_transport_mode);
            var vesselVoyage = JoinNonEmpty(" / ", Trim(consol?.vessel), Trim(consol?.voyage));

            var model = new InvoicePdfTemplateDto
            {
                Title = isAp ? "PAYMENT VOUCHER" : "INVOICE",
                Watermark = head.ah_postdate.HasValue ? string.Empty : "DRAFT",

                CompanyName = Trim(letterhead?.branch_name) ?? Trim(letterhead?.company_name),
                CompanyAddress1 = Trim(letterhead?.branch_addr1) ?? Trim(letterhead?.company_addr1),
                CompanyAddress2 = JoinNonEmpty(" ",
                    Trim(letterhead?.branch_addr2) ?? Trim(letterhead?.company_addr2),
                    Trim(letterhead?.branch_city) ?? Trim(letterhead?.company_city),
                    Trim(letterhead?.branch_postcode) ?? Trim(letterhead?.company_postcode)),
                CompanyPhone = Trim(letterhead?.branch_phone) ?? Trim(letterhead?.company_phone),

                BillingPartyCompanyName = Trim(party?.name),
                BillingPartyAddress = JoinNonEmpty(", ",
                    Trim(party?.addr1), Trim(party?.addr2), Trim(party?.city), Trim(party?.state),
                    Trim(party?.postcode), Trim(party?.country)),

                InvoiceNo = Trim(head.ah_transactionnum) ?? Trim(head.ah_consolidatedinvoiceref),
                InvoiceDate = FormatDate(head.ah_invoicedate),
                DueDate = FormatDate(head.ah_duedate),
                IssuedDate = FormatDate(head.ah_postdate ?? head.ah_systemcreatetimeutc ?? head.ah_invoicedate),
                IssuedBy = Trim(issuer) ?? Trim(head.ah_systemcreateuser),

                ShipperName = Trim(addresses.FirstOrDefault(x => x.addr_type == "CRD")?.name),
                ConsigneeName = Trim(addresses.FirstOrDefault(x => x.addr_type == "CEG")?.name),
                ShipmentNo = Trim(job?.job_num) ?? Trim(head.ah_jobnumber),

                FreightTerms = Trim(head.ah_invoiceterm),
                Terms = JoinNonEmpty(" ", Trim(head.ah_invoiceterm),
                    head.ah_invoicetermdays.GetValueOrDefault() > 0 ? $"{head.ah_invoicetermdays} DAYS" : null),

                TransportMode = transportMode,
                VesselVoyage = vesselVoyage,
                ConsolNo = Trim(consol?.consol_no),
                MblNo = Trim(consol?.mbl),
                HblNo = Trim(job?.hbl),

                Etd = FormatDate(consol?.etd),
                Eta = FormatDate(consol?.eta),
                Origin = Trim(job?.origin),
                Destination = Trim(job?.destination),

                GrossWeight = FormatQuantity(job?.weight, Trim(job?.weight_unit)),
                Cbm = FormatQuantity(job?.volume, Trim(job?.volume_unit)),
                // 计费重只有空运才是重量单位（KG），海运是计费吨，不带单位免得标错
                ChargeableWeight = FormatQuantity(job?.chargeable,
                    string.Equals(transportMode, "AIR", StringComparison.OrdinalIgnoreCase) ? Trim(job?.weight_unit) : null),
                Packages = job?.packs > 0 ? job.packs.ToString() : null,

                ContainerSealNos = containers.Count == 0
                    ? null
                    : string.Join(", ", containers
                        .Where(x => !string.IsNullOrWhiteSpace(x.container_no))
                        .Select(x => JoinNonEmpty("/", Trim(x.container_no), Trim(x.container_type)))),
                ContainerCount = containers.Count > 0 ? containers.Count.ToString() : null,

                Currency = currency,
                SubTotal = FormatMoney(subTotal, currency),
                Vat = tax == 0 ? null : FormatMoney(tax, currency),
                Total = FormatMoney(total, currency),
                HomeCurrency = Trim(letterhead?.home_currency),
                HomeAmount = FormatMoney(head.ah_localtotal.GetValueOrDefault(), Trim(letterhead?.home_currency)),

                BankBeneficiary = Trim(bank?.beneficiary),
                BankName = Trim(bank?.bank_name),
                BankAddress = Trim(bank?.bank_address),
                BankAccountNumber = Trim(bank?.account_no),
                BankSwift = Trim(bank?.swift)
            };

            foreach (var line in lines)
            {
                // 单价为 0 的行（CargoWise 直接按金额记账）不打印单价与币种，避免出现孤零零的币种
                var unitPrice = line.unit_price.GetValueOrDefault();

                model.ChargeLines.Add(new InvoiceChargeLineDto
                {
                    ChargeCode = Trim(line.charge_code),
                    Description = Trim(line.line_desc)
                        ?? Trim(line.charge_desc_local)
                        ?? Trim(line.charge_desc)
                        ?? Trim(line.charge_code),
                    UnitPriceCurrency = unitPrice == 0 ? null : (Trim(line.currency) ?? currency),
                    UnitPrice = unitPrice == 0 ? null : FormatNumber(unitPrice),
                    Qty = line.qty.GetValueOrDefault() == 0 ? null : line.qty.ToString(),
                    Unit = null,
                    ExchangeRate = line.exchange_rate.GetValueOrDefault() == 0
                        ? null
                        : line.exchange_rate.GetValueOrDefault().ToString("0.####", CultureInfo.InvariantCulture),
                    InvoiceAmount = FormatNumber(line.amount.GetValueOrDefault())
                });
            }

            return model;
        }

        private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string JoinNonEmpty(string separator, params string[] parts)
        {
            var kept = parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            return kept.Count == 0 ? null : string.Join(separator, kept);
        }

        private static string FormatDate(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string FormatNumber(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);

        private static string FormatMoney(decimal value, string currency)
        {
            var amount = FormatNumber(value);
            return string.IsNullOrWhiteSpace(currency) ? amount : $"{currency} {amount}";
        }

        private static string FormatQuantity(decimal? value, string unit)
        {
            if (!value.HasValue || value.Value == 0)
            {
                return null;
            }

            var amount = value.Value.ToString("#,##0.###", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(unit) ? amount : $"{amount} {unit}";
        }

#pragma warning disable IDE1006 // 与 SQL 列别名保持一致，便于 Dapper 映射
        private class HeadRow
        {
            public string ah_pk { get; set; }
            public string ah_transactionnum { get; set; }
            public string ah_consolidatedinvoiceref { get; set; }
            public string ah_ledger { get; set; }
            public DateTime? ah_invoicedate { get; set; }
            public DateTime? ah_duedate { get; set; }
            public DateTime? ah_postdate { get; set; }
            public decimal? ah_gstamount { get; set; }
            public decimal? ah_ostotal { get; set; }
            public decimal? ah_localtotal { get; set; }
            public string ah_rx_nktransactioncurrency { get; set; }
            public decimal? ah_exchangerate { get; set; }
            public string ah_invoiceterm { get; set; }
            public int? ah_invoicetermdays { get; set; }
            public string ah_jobnumber { get; set; }
            public string ah_oh { get; set; }
            public string ah_jh { get; set; }
            public string ah_gb { get; set; }
            public string ah_gc { get; set; }
            public string ah_ab { get; set; }
            public string ah_oa_invoiceaddressoverride { get; set; }
            public string ah_systemcreateuser { get; set; }
            public DateTime? ah_systemcreatetimeutc { get; set; }
        }

        private class LineRow
        {
            public short? seq { get; set; }
            public string line_desc { get; set; }
            public int? qty { get; set; }
            public decimal? unit_price { get; set; }
            public decimal? amount { get; set; }
            public decimal? amount_local { get; set; }
            public string currency { get; set; }
            public decimal? exchange_rate { get; set; }
            public decimal? gst { get; set; }
            public string charge_code { get; set; }
            public string charge_desc { get; set; }
            public string charge_desc_local { get; set; }
        }

        private class PartyRow
        {
            public string code { get; set; }
            public string name { get; set; }
            public string addr1 { get; set; }
            public string addr2 { get; set; }
            public string city { get; set; }
            public string state { get; set; }
            public string postcode { get; set; }
            public string country { get; set; }
        }

        private class LetterheadRow
        {
            public string branch_name { get; set; }
            public string branch_addr1 { get; set; }
            public string branch_addr2 { get; set; }
            public string branch_city { get; set; }
            public string branch_state { get; set; }
            public string branch_postcode { get; set; }
            public string branch_phone { get; set; }
            public string company_name { get; set; }
            public string company_addr1 { get; set; }
            public string company_addr2 { get; set; }
            public string company_city { get; set; }
            public string company_state { get; set; }
            public string company_postcode { get; set; }
            public string company_phone { get; set; }
            public string home_currency { get; set; }
        }

        private class JobRow
        {
            public string job_num { get; set; }
            public string job_transport_mode { get; set; }
            public string hbl { get; set; }
            public string transport_mode { get; set; }
            public string origin { get; set; }
            public string destination { get; set; }
            public decimal? weight { get; set; }
            public string weight_unit { get; set; }
            public decimal? volume { get; set; }
            public string volume_unit { get; set; }
            public decimal? chargeable { get; set; }
            public int? packs { get; set; }
        }

        private class ConsolRow
        {
            public string consol_no { get; set; }
            public string mbl { get; set; }
            public string vessel { get; set; }
            public string voyage { get; set; }
            public DateTime? etd { get; set; }
            public DateTime? eta { get; set; }
        }

        private class DocAddressRow
        {
            public string addr_type { get; set; }
            public string name { get; set; }
        }

        private class ContainerRow
        {
            public string container_no { get; set; }
            public string container_type { get; set; }
        }

        private class BankRow
        {
            public int priority { get; set; }
            public string beneficiary { get; set; }
            public string bank_name { get; set; }
            public string bank_address { get; set; }
            public string account_no { get; set; }
            public string swift { get; set; }
        }
#pragma warning restore IDE1006
    }
}
