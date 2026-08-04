using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Consolidation.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SntBackend.Application.Consolidation
{
    /// <summary>
    /// 合单(JobConsol)账单：与 shipment 侧 <see cref="Billing.IBillingApplication"/> 一一对应的能力，
    /// 实现全部委托 <see cref="Billing.BillingCore"/>，只把作用域固定为 BillingScope.Consol。
    /// 下拉框类接口（费用代码/分支/税率/VAT/本位币）与锚点无关，继续用 /billing/* 那几个，不再重复一份。
    /// </summary>
    public interface IConsolidationBillingApplication : ISntBackendApplicationBase
    {
        /// <summary>按合单 + AR/AP 分页查询费用行（JobCharge）。</summary>
        Task<BillingChargeLineOutput> QueryChargeLine(ConsolBillingChargeLineInput input);

        /// <summary>按合单 + AR/AP 分页查询发票头（含 INV 与作废后生成的 CRD）。</summary>
        Task<BillingDraftPageOutput> QueryDraftPage(ConsolBillingDraftPageInput input);

        /// <summary>合单账单汇总（毛利率、AR、AP、利润）。</summary>
        Task<BillingSummaryDto> GetBillingSummary(string jkPk);

        /// <summary>新增 / 修改合单应收应付费用（JobCharge）。合单作业头不存在时按需创建。</summary>
        Task<BillingCreateOrUpdateOutput> CreateOrUpdate(ConsolBillingCreateInput input);

        /// <summary>生成草稿发票，返回新建的发票号列表。</summary>
        Task<List<string>> GenerateDraft(GenerateDraftInput input);

        /// <summary>直接过账，返回过账成功的发票头数量。</summary>
        Task<int> PostCharge(PostChargeInput input);

        /// <summary>批量删除费用（仅未开票行）。</summary>
        Task<int> Delete(List<string> jrPks);

        /// <summary>作废草稿发票（未过账）。</summary>
        Task<int> VoidDraftInvoice(VoidInvoiceInput input);

        /// <summary>作废正式账单（已过账）：走库内既有的 CRD 冲销流程，见 <see cref="Billing.IBillingApplication.VoidPostedInvoice"/>。</summary>
        Task<int> VoidPostedInvoice(List<string> invoiceNos, string reason = null, string reasonDesc = null);

        /// <summary>编辑草稿发票（删除/修改/新增费用）。</summary>
        Task<int> EditDraftInvoice(DraftInvoiceEditInput input);

        /// <summary>按发票号查关联的费用明细（与锚点无关，转发 shipment 侧实现）。</summary>
        Task<QueryChargesByInvoiceOutput> QueryChargesByInvoiceNo(string invoiceNo);

        /// <summary>发票打印（与锚点无关，转发 shipment 侧实现）。</summary>
        Task<GenerateInvoicePdfOutput> GenerateInvoicePdf(GenerateInvoicePdfInput input);
    }
}
