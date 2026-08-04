using SntBackend.Application.Billing;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Consolidation.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SntBackend.Application.Consolidation
{
    /// <summary>
    /// 合单账单实现：账单逻辑一份在 <see cref="BillingCore"/>，这里只固定 <see cref="BillingScope.Consol"/>。
    /// 与锚点无关的能力（按发票号反查费用、发票打印）直接转发 shipment 侧的 <see cref="IBillingApplication"/>。
    /// </summary>
    public class ConsolidationBillingApplication : SntBackendApplicationBase, IConsolidationBillingApplication
    {
        private readonly BillingCore _billingCore;
        private readonly IBillingApplication _billingApplication;

        public ConsolidationBillingApplication(BillingCore billingCore, IBillingApplication billingApplication)
        {
            _billingCore = billingCore;
            _billingApplication = billingApplication;
        }

        public Task<BillingChargeLineOutput> QueryChargeLine(ConsolBillingChargeLineInput input) =>
            _billingCore.QueryChargeLineAsync(BillingScope.Consol, input?.jkPk, input?.chargeType,
                input?.SkipCount ?? 0, input?.MaxResultCount ?? 20, input?.Sorting);

        public Task<BillingDraftPageOutput> QueryDraftPage(ConsolBillingDraftPageInput input) =>
            _billingCore.QueryDraftPageAsync(BillingScope.Consol, input?.jkPk, input?.chargeType,
                input?.SkipCount ?? 0, input?.MaxResultCount ?? 20, input?.Sorting);

        public Task<BillingSummaryDto> GetBillingSummary(string jkPk) =>
            _billingCore.GetBillingSummaryAsync(BillingScope.Consol, jkPk);

        public Task<BillingCreateOrUpdateOutput> CreateOrUpdate(ConsolBillingCreateInput input) =>
            _billingCore.CreateOrUpdateAsync(BillingScope.Consol, input?.jkPk, input?.charges);

        public Task<List<string>> GenerateDraft(GenerateDraftInput input) =>
            _billingCore.GenerateDraftAsync(BillingScope.Consol, input?.pks, input?.chargeType);

        public Task<int> PostCharge(PostChargeInput input) =>
            _billingCore.PostChargeAsync(BillingScope.Consol, input?.pks, input?.chargeType);

        public Task<int> Delete(List<string> jrPks) =>
            _billingCore.DeleteAsync(jrPks);

        public Task<int> VoidDraftInvoice(VoidInvoiceInput input) =>
            _billingCore.VoidDraftInvoiceAsync(input?.ahPks);

        public Task<int> VoidPostedInvoice(List<string> invoiceNos, string reason = null, string reasonDesc = null) =>
            _billingCore.VoidPostedInvoiceAsync(invoiceNos, reason, reasonDesc);

        public Task<int> EditDraftInvoice(DraftInvoiceEditInput input) =>
            _billingCore.EditDraftInvoiceAsync(input);

        public Task<QueryChargesByInvoiceOutput> QueryChargesByInvoiceNo(string invoiceNo) =>
            _billingApplication.QueryChargesByInvoiceNo(invoiceNo);

        public Task<GenerateInvoicePdfOutput> GenerateInvoicePdf(GenerateInvoicePdfInput input) =>
            _billingApplication.GenerateInvoicePdf(input);
    }
}
