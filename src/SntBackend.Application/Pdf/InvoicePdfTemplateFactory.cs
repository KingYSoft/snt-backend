using SntBackend.Application.Pdf.Dto;
using System.Net;
using System.Text;

namespace SntBackend.Application.Pdf
{
    /// <summary>
    /// 把发票渲染模型拼成 HTML。
    /// 排版沿用 first-cargo-backend 的 PdfTemplates/Billing/PDF_INV_000001 模板，
    /// 差别是这里不走 RazorLight，直接在代码里拼串（snt 没有模板表和模板文件）。
    /// </summary>
    public static class InvoicePdfTemplateFactory
    {
        /// <summary>
        /// 生成整页发票 HTML。
        /// </summary>
        public static string BuildHtml(InvoicePdfTemplateDto model)
        {
            var sb = new StringBuilder();

            sb.Append(@"<!DOCTYPE html>
<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"">
<title>Invoice</title>
<style type=""text/css"">
@page { size: A4 portrait; margin: 10mm 10mm 8mm 10mm; }
html, body { margin: 0; padding: 0; background: #fff; }
body, div, table, thead, tbody, tr, th, td, p {
    font-family: ""Noto Sans SC"", ""PingFang SC"", ""Microsoft YaHei"", Arial, Helvetica, sans-serif;
    font-size: 11px;
}
.invoice-parent-container { width: 190mm; margin: 0 auto; padding: 0; position: relative; isolation: isolate; box-sizing: border-box; }
.company-header { width: 100%; overflow: hidden; padding-bottom: 8px; }
.company-name { font-size: 12pt; font-weight: 700; letter-spacing: .5px; line-height: 1.15; color: #1007A0; margin: 0 0 8px 0; }
.company-address, .company-phone { font-size: 7pt; font-weight: 500; line-height: 1.35; color: #4090F7; margin: 0; }
.company-phone { margin-top: 2px; }
.first-container-invoice-info { width: 100%; box-sizing: border-box; border-top: 2px solid #000; }
.invoice-title { text-align: center; padding-top: 1%; margin-bottom: 1%; font-size: 25px; font-weight: 700; letter-spacing: 1px; line-height: 1.2; }
.invoice-top-flex { display: flex; align-items: stretch; justify-content: space-between; gap: 18px; }
.invoice-top-left { flex: 1; box-sizing: border-box; }
.invoice-top-left .to-label { font-size: 11px; margin-bottom: 6px; line-height: 1; }
.invoice-top-left .to-address { font-size: 11px; line-height: 1.4; word-break: break-word; text-transform: uppercase; }
.invoice-top-right { flex: 1; display: flex; flex-direction: column; gap: 3px; justify-content: flex-end; text-align: left; }
.first-info-row { display: flex; align-items: flex-start; }
.first-info-row .label { width: 125px; font-size: 11px; line-height: 1.4; box-sizing: border-box; white-space: nowrap; }
.first-info-row .value { flex: 1; font-size: 11px; line-height: 1.4; word-break: break-word; text-transform: uppercase; }
.second-container-invoice-info { width: 100%; margin-top: 6px; padding-top: 6px; border-top: 1px solid #000; box-sizing: border-box; }
.second-pair-row { display: flex; justify-content: space-between; align-items: flex-start; gap: 18px; box-sizing: border-box; }
.second-pair-row .left, .second-pair-row .right { width: 50%; display: flex; flex-direction: column; gap: 6px; text-align: left; }
.pair-item { display: flex; align-items: flex-start; min-width: 0; margin-top: 4px; }
.pair-item .label { width: 132px; font-size: 11px; line-height: 1; white-space: nowrap; box-sizing: border-box; padding-right: 6px; }
.pair-item .value { flex: 1; min-width: 0; font-size: 11px; line-height: 1.2; word-break: break-word; text-transform: uppercase; }
.invoice-main-content { display: block; border-top: 1px solid #000; margin-top: 4px; }
.invoice-table { width: 100%; border-collapse: collapse; table-layout: fixed; }
.invoice-table thead th { padding: 6px 4px; font-size: 11px; font-weight: 500; text-align: center; vertical-align: middle; line-height: 1.25; word-break: break-word; background-color: #9cf; }
.invoice-table thead th:first-child, .invoice-table thead th:nth-child(2) { text-align: left; }
.invoice-table thead th:last-child { text-align: right; }
.invoice-table tbody td { padding: 4px 0; font-size: 11px; line-height: 1.25; vertical-align: top; word-break: break-word; text-transform: uppercase; box-sizing: border-box; }
.invoice-table tbody td:nth-child(1) { text-align: left; }
.invoice-table tbody td:nth-child(2) { text-align: left; vertical-align: middle; }
.invoice-table tbody td:nth-child(3), .invoice-table tbody td:nth-child(4), .invoice-table tbody td:nth-child(5) { text-align: center; vertical-align: middle; }
.invoice-table .col-1 { width: 36%; } .invoice-table .col-2 { width: 13%; } .invoice-table .col-3 { width: 10%; }
.invoice-table .col-4 { width: 7%; } .invoice-table .col-5 { width: 10%; } .invoice-table .col-6 { width: 18%; }
.invoice-table tbody td.amount-cell { padding-left: 4px; padding-right: 4px; }
.invoice-table .money-wrap { display: flex; align-items: center; justify-content: space-between; width: 100%; line-height: 1; }
.invoice-table .money-wrap.currency-right { justify-content: flex-end; gap: 4px; }
.invoice-summary { display: flex; flex-direction: column; align-items: flex-end; margin-top: 2px; font-size: 11px; line-height: 1.6; border-top: 1px solid #000; }
.invoice-summary .summary-row { display: inline-flex; justify-content: flex-end; align-items: center; min-width: 260px; white-space: nowrap; margin-top: 8px; }
.invoice-summary .summary-label { text-align: left; white-space: nowrap; }
.invoice-summary .summary-value { flex: 0 0 auto; min-width: 135px; text-align: right; white-space: nowrap; }
.invoice-summary .summary-row-total { font-weight: bold; }
.invoice-footer-block { break-inside: avoid; page-break-inside: avoid; }
.five-container-bank-info { width: 100%; margin-top: 8px; border-top: 1px solid #000; box-sizing: border-box; }
.five-pair-row { display: flex; justify-content: space-between; align-items: flex-start; gap: 20px; padding: 3px; box-sizing: border-box; }
.five-pair-item { flex: 0 0 calc(50% - 10px); display: flex; align-items: flex-start; min-width: 0; }
.five-pair-item .label { font-size: 11px; line-height: 1.2; white-space: nowrap; padding-right: 4px; box-sizing: border-box; }
.five-pair-item .value { flex: 1; min-width: 0; font-size: 11px; line-height: 1.2; word-break: break-word; text-transform: uppercase; }
.sheet-watermark { position: fixed; left: 50%; top: 56%; transform: translate(-50%, -50%) rotate(-32deg); transform-origin: center; z-index: 9999; pointer-events: none; white-space: nowrap; }
.sheet-watermark span { display: inline-block; font-size: 120px; font-weight: 700; text-transform: uppercase; letter-spacing: 18px; color: rgba(120,120,120,.16); line-height: 1; }
</style>
</head>
<body>
<div class=""invoice-parent-container"">
");

            if (!string.IsNullOrWhiteSpace(model.Watermark))
            {
                sb.Append("    <div class=\"sheet-watermark\"><span>").Append(E(model.Watermark)).Append("</span></div>\n");
            }

            // 抬头
            sb.Append(@"    <div class=""company-header"">
        <div class=""company-info"">
            <div class=""company-name"">").Append(E(model.CompanyName)).Append(@"</div>
            <div class=""company-address"">").Append(E(model.CompanyAddress1)).Append(@"</div>
            <div class=""company-address"">").Append(E(model.CompanyAddress2)).Append(@"</div>
            <div class=""company-phone"">Phone: ").Append(E(model.CompanyPhone)).Append(@"</div>
        </div>
    </div>
");

            // 第一块：账单方 + 发票基础信息
            sb.Append(@"    <div class=""first-container-invoice-info"">
        <div class=""invoice-title"">-").Append(E(model.Title)).Append(@"-</div>
        <div class=""invoice-top-flex"">
            <div class=""invoice-top-left"">
                <div class=""to-label"">To: ").Append(E(model.BillingPartyCompanyName)).Append(@"</div>
                <br />
                <div class=""to-address"">").Append(E(model.BillingPartyAddress)).Append(@"</div>
            </div>
            <div class=""invoice-top-right"">
");
            AppendInfoRow(sb, "INVOICE NO.:", model.InvoiceNo);
            AppendInfoRow(sb, "INVOICE DATE:", model.InvoiceDate);
            AppendInfoRow(sb, "SHIPPER:", model.ShipperName);
            AppendInfoRow(sb, "CONSIGNEE:", model.ConsigneeName);
            AppendInfoRow(sb, "SHIPMENT NO.:", model.ShipmentNo);
            AppendInfoRow(sb, "FREIGHT TERM:", model.FreightTerms);
            sb.Append(@"            </div>
        </div>
    </div>
");

            // 第二块：运输信息
            var isAir = model.TransportMode == "AIR";
            sb.Append(@"    <div class=""second-container-invoice-info"">
        <div class=""second-pair-row"">
            <div class=""left"">
");
            AppendPairItem(sb, isAir ? "FLIGHT NO.:" : "VSL/VOY NO.:", model.VesselVoyage);
            AppendPairItem(sb, "CONSOL NO:", model.ConsolNo);
            AppendPairItem(sb, isAir ? "MAWB:" : "MB/L NO:", model.MblNo);
            AppendPairItem(sb, isAir ? "HAWB:" : "HB/L NO:", model.HblNo);
            AppendPairItem(sb, "GROSS WEIGHT:", model.GrossWeight);
            AppendPairItem(sb, "CBM:", model.Cbm);
            AppendPairItem(sb, "CHARGEABLE:", model.ChargeableWeight);
            AppendPairItem(sb, "NO. OF PACKAGE:", model.Packages);
            sb.Append(@"            </div>
            <div class=""right"">
");
            AppendPairItem(sb, "ETD:", model.Etd);
            AppendPairItem(sb, "ETA:", model.Eta);
            AppendPairItem(sb, "ORIGIN:", model.Origin);
            AppendPairItem(sb, "DESTINATION:", model.Destination);
            AppendPairItem(sb, "TERM:", model.Terms);
            AppendPairItem(sb, "DUE DATE:", model.DueDate);
            AppendPairItem(sb, "ISSUED DATE:", model.IssuedDate);
            AppendPairItem(sb, "ISSUED BY:", model.IssuedBy);
            sb.Append(@"            </div>
        </div>
");
            if (!string.IsNullOrWhiteSpace(model.ContainerSealNos))
            {
                sb.Append(@"        <div class=""second-pair-row"" style=""margin-top:6px;"">
            <div class=""pair-item""><div class=""label"">CONTAINER NO./SIZE:</div><div class=""value"">")
                    .Append(E(model.ContainerSealNos)).Append("</div></div>\n        </div>\n");
                sb.Append(@"        <div class=""second-pair-row"" style=""margin-top:6px;"">
            <div class=""pair-item""><div class=""label"">NO.OF CONTAINER(S):</div><div class=""value"">")
                    .Append(E(model.ContainerCount)).Append("</div></div>\n        </div>\n");
            }
            sb.Append("    </div>\n");

            // 第三块：费用明细
            var chargesHeader = string.IsNullOrWhiteSpace(model.Currency)
                ? "Charges In"
                : $"Charges In ({model.Currency})";

            sb.Append(@"    <div class=""invoice-main-content"">
        <table class=""invoice-table"">
            <colgroup>
                <col class=""col-1""><col class=""col-2""><col class=""col-3"">
                <col class=""col-4""><col class=""col-5""><col class=""col-6"">
            </colgroup>
            <thead>
                <tr>
                    <th>Description</th>
                    <th>Unit Price</th>
                    <th>QTY</th>
                    <th>Unit</th>
                    <th>Ex. Rate</th>
                    <th>").Append(E(chargesHeader)).Append(@"</th>
                </tr>
            </thead>
            <tbody>
");
            foreach (var line in model.ChargeLines)
            {
                sb.Append("                <tr>\n")
                  .Append("                    <td style=\"white-space: pre-line;\">").Append(E(line.Description)).Append("</td>\n")
                  .Append("                    <td class=\"amount-cell\"><div class=\"money-wrap\"><span class=\"currency\">")
                  .Append(E(line.UnitPriceCurrency)).Append("</span><span class=\"amount\">")
                  .Append(E(line.UnitPrice)).Append("</span></div></td>\n")
                  .Append("                    <td>").Append(E(line.Qty)).Append("</td>\n")
                  .Append("                    <td>").Append(E(line.Unit)).Append("</td>\n")
                  .Append("                    <td class=\"amount-cell\">").Append(E(line.ExchangeRate)).Append("</td>\n")
                  .Append("                    <td class=\"amount-cell\"><div class=\"money-wrap currency-right\"><span class=\"amount\">")
                  .Append(E(line.InvoiceAmount)).Append("</span></div></td>\n")
                  .Append("                </tr>\n");
            }
            sb.Append(@"            </tbody>
        </table>
    </div>
");

            // 第四块：合计
            sb.Append(@"    <div class=""invoice-summary"">
        <div class=""summary-row""><div class=""summary-label"">Sub Total</div><div class=""summary-value"">")
                .Append(E(model.SubTotal)).Append("</div></div>\n");
            if (!string.IsNullOrWhiteSpace(model.Vat))
            {
                sb.Append(@"        <div class=""summary-row""><div class=""summary-label"">VAT</div><div class=""summary-value"">")
                    .Append(E(model.Vat)).Append("</div></div>\n");
            }
            sb.Append(@"        <div class=""summary-row summary-row-total""><div class=""summary-label"">Total</div><div class=""summary-value"">")
                .Append(E(model.Total)).Append("</div></div>\n");
            if (!string.IsNullOrWhiteSpace(model.HomeCurrency)
                && !string.Equals(model.HomeCurrency, model.Currency, System.StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(@"        <div class=""summary-row""><div class=""summary-label"">Home Amount</div><div class=""summary-value"">")
                    .Append(E(model.HomeAmount)).Append("</div></div>\n");
            }
            sb.Append("    </div>\n");

            // 第五块：银行信息
            sb.Append(@"    <div class=""invoice-footer-block"">
        <div class=""five-container-bank-info"">
            <div class=""five-pair-row"">
                <div class=""five-pair-item""><div class=""label"">Bank Information:</div><div class=""value""></div></div>
            </div>
            <div class=""five-pair-row"">
                <div class=""five-pair-item""><div class=""label"">SWIFT Code:</div><div class=""value"">").Append(E(model.BankSwift)).Append(@"</div></div>
                <div class=""five-pair-item""><div class=""label"">NAME OF BANK:</div><div class=""value"">").Append(E(model.BankName)).Append(@"</div></div>
            </div>
            <div class=""five-pair-row"">
                <div class=""five-pair-item""><div class=""label"">Beneficiary:</div><div class=""value"">").Append(E(model.BankBeneficiary)).Append(@"</div></div>
                <div class=""five-pair-item""><div class=""label"">Bank's Address:</div><div class=""value"">").Append(E(model.BankAddress)).Append(@"</div></div>
            </div>
            <div class=""five-pair-row"">
                <div class=""five-pair-item""></div>
                <div class=""five-pair-item""><div class=""label"">Account Number:</div><div class=""value"">").Append(E(model.BankAccountNumber)).Append(@"</div></div>
            </div>
        </div>
    </div>
</div>
</body>
</html>");

            return sb.ToString();
        }

        /// <summary>
        /// 追加发票基础信息里的一行。
        /// </summary>
        private static void AppendInfoRow(StringBuilder sb, string label, string value)
        {
            sb.Append("                <div class=\"first-info-row\"><div class=\"label\">")
              .Append(E(label))
              .Append("</div><div class=\"value\">")
              .Append(E(value))
              .Append("</div></div>\n");
        }

        /// <summary>
        /// 追加运输信息里的一行。
        /// </summary>
        private static void AppendPairItem(StringBuilder sb, string label, string value)
        {
            sb.Append("                <div class=\"pair-item\"><div class=\"label\">")
              .Append(E(label))
              .Append("</div><div class=\"value\">")
              .Append(E(value))
              .Append("</div></div>\n");
        }

        /// <summary>
        /// HTML 转义，null 输出空串。
        /// </summary>
        private static string E(string value) => string.IsNullOrEmpty(value) ? string.Empty : WebUtility.HtmlEncode(value);
    }
}
