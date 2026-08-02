using System.Collections.Generic;

namespace SntBackend.Application.Pdf.Dto
{
    /// <summary>
    /// 发票 PDF 渲染模型。字段与 first-cargo-backend 的 InvoicePdfTemplateDto 对齐，
    /// 只保留 snt 库里能取到值的部分。
    /// </summary>
    public class InvoicePdfTemplateDto
    {
        /// <summary>发票标题，AR=INVOICE，AP=PAYMENT VOUCHER。</summary>
        public string Title { get; set; }

        /// <summary>水印文字，未过账（ah_postdate 为空）时为 DRAFT。</summary>
        public string Watermark { get; set; }

        /// <summary>抬头公司名（GlbBranch.gb_branchname，回落 GlbCompany.gc_name）。</summary>
        public string CompanyName { get; set; }

        /// <summary>抬头地址第一行。</summary>
        public string CompanyAddress1 { get; set; }

        /// <summary>抬头地址第二行（含城市 / 省 / 邮编）。</summary>
        public string CompanyAddress2 { get; set; }

        /// <summary>抬头电话。</summary>
        public string CompanyPhone { get; set; }

        /// <summary>账单方名称（OrgHeader.oh_fullname）。</summary>
        public string BillingPartyCompanyName { get; set; }

        /// <summary>账单方地址。</summary>
        public string BillingPartyAddress { get; set; }

        /// <summary>发票号。</summary>
        public string InvoiceNo { get; set; }

        /// <summary>发票日期。</summary>
        public string InvoiceDate { get; set; }

        /// <summary>发货人（JobDocAddress 地址类型 CRD）。</summary>
        public string ShipperName { get; set; }

        /// <summary>收货人（JobDocAddress 地址类型 CEG）。</summary>
        public string ConsigneeName { get; set; }

        /// <summary>业务单号（JobHeader.jh_jobnum，回落 ah_jobnumber）。</summary>
        public string ShipmentNo { get; set; }

        /// <summary>付款条款（ah_invoiceterm）。</summary>
        public string FreightTerms { get; set; }

        /// <summary>发票条款（ah_invoiceterm + 账期天数）。</summary>
        public string Terms { get; set; }

        /// <summary>运输方式，AIR / SEA。</summary>
        public string TransportMode { get; set; }

        /// <summary>船名航次 / 航班号。</summary>
        public string VesselVoyage { get; set; }

        /// <summary>拼箱号（JobConsol.jk_uniqueconsignref）。</summary>
        public string ConsolNo { get; set; }

        /// <summary>主提单号（JobConsol.jk_masterbillnum）。</summary>
        public string MblNo { get; set; }

        /// <summary>分提单号（JobShipment.js_housebill）。</summary>
        public string HblNo { get; set; }

        /// <summary>箱号 / 箱型，多箱以逗号分隔。</summary>
        public string ContainerSealNos { get; set; }

        /// <summary>箱量。</summary>
        public string ContainerCount { get; set; }

        /// <summary>毛重。</summary>
        public string GrossWeight { get; set; }

        /// <summary>体积。</summary>
        public string Cbm { get; set; }

        /// <summary>体积重。</summary>
        public string VolumeWeight { get; set; }

        /// <summary>计费重。</summary>
        public string ChargeableWeight { get; set; }

        /// <summary>件数。</summary>
        public string Packages { get; set; }

        /// <summary>预计开船 / 起飞时间。</summary>
        public string Etd { get; set; }

        /// <summary>预计到港 / 到达时间。</summary>
        public string Eta { get; set; }

        /// <summary>起运地。</summary>
        public string Origin { get; set; }

        /// <summary>目的地。</summary>
        public string Destination { get; set; }

        /// <summary>到期日。</summary>
        public string DueDate { get; set; }

        /// <summary>签发日期。</summary>
        public string IssuedDate { get; set; }

        /// <summary>签发人（GlbStaff.gs_fullname）。</summary>
        public string IssuedBy { get; set; }

        /// <summary>发票币种。</summary>
        public string Currency { get; set; }

        /// <summary>费用明细行。</summary>
        public List<InvoiceChargeLineDto> ChargeLines { get; set; } = new List<InvoiceChargeLineDto>();

        /// <summary>小计（不含税，发票币种）。</summary>
        public string SubTotal { get; set; }

        /// <summary>税额（发票币种），为 0 时留空不打印该行。</summary>
        public string Vat { get; set; }

        /// <summary>合计（含税，发票币种）。</summary>
        public string Total { get; set; }

        /// <summary>本位币币种（GlbCompany.gc_rx_nklocalcurrency）。</summary>
        public string HomeCurrency { get; set; }

        /// <summary>本位币合计（ah_localtotal）。</summary>
        public string HomeAmount { get; set; }

        /// <summary>银行户名。</summary>
        public string BankBeneficiary { get; set; }

        /// <summary>银行名称。</summary>
        public string BankName { get; set; }

        /// <summary>银行 SWIFT Code。</summary>
        public string BankSwift { get; set; }

        /// <summary>银行账号。</summary>
        public string BankAccountNumber { get; set; }

        /// <summary>银行地址。</summary>
        public string BankAddress { get; set; }
    }

    /// <summary>
    /// 发票 PDF 的费用明细行。
    /// </summary>
    public class InvoiceChargeLineDto
    {
        /// <summary>费用代码（AccChargeCode.ac_code）。</summary>
        public string ChargeCode { get; set; }

        /// <summary>费用描述。</summary>
        public string Description { get; set; }

        /// <summary>单价币种。</summary>
        public string UnitPriceCurrency { get; set; }

        /// <summary>单价。</summary>
        public string UnitPrice { get; set; }

        /// <summary>数量。</summary>
        public string Qty { get; set; }

        /// <summary>单位。</summary>
        public string Unit { get; set; }

        /// <summary>汇率。</summary>
        public string ExchangeRate { get; set; }

        /// <summary>发票币种金额。</summary>
        public string InvoiceAmount { get; set; }
    }
}
