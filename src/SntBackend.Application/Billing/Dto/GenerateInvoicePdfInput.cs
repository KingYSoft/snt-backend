using System.Collections.Generic;

namespace SntBackend.Application.Billing.Dto
{
    /// <summary>
    /// 发票打印（生成 PDF）入参。
    /// </summary>
    public class GenerateInvoicePdfInput
    {
        /// <summary>
        /// 发票号列表。按 AccTransactionHeader.ah_transactionnum 匹配，
        /// 匹配不到时回落 ah_consolidatedinvoiceref（AP 合并发票号）。
        /// </summary>
        public List<string> invoice_nos { get; set; }

        /// <summary>
        /// 账本类型：AR / AP。留空则不按账本过滤。
        /// </summary>
        public string ledger_type { get; set; }
    }

    /// <summary>
    /// 发票打印出参。
    /// </summary>
    public class GenerateInvoicePdfOutput
    {
        /// <summary>
        /// 每张发票一条生成结果。
        /// </summary>
        public List<InvoicePdfResult> results { get; set; } = new List<InvoicePdfResult>();
    }

    /// <summary>
    /// 单张发票的 PDF 生成结果。
    /// </summary>
    public class InvoicePdfResult
    {
        /// <summary>
        /// 发票号。
        /// </summary>
        public string invoice_no { get; set; }

        /// <summary>
        /// PDF 相对访问路径，形如 /files/pdf/202608/INV_xxx_20260802103000.pdf。
        /// </summary>
        public string pdf_path { get; set; }
    }
}
