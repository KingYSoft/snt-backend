using SntBackend.Application.Billing;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Consolidation.Dto;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SntBackend.Application.Consolidation
{
    /// <summary>
    /// 合单账单实现：账单逻辑一份在 <see cref="BillingCore"/>，这里只固定 <see cref="BillingScope.Consol"/>。
    /// 与锚点无关的能力（按发票号反查费用、发票打印）直接转发 shipment 侧的 <see cref="IBillingApplication"/>。
    ///
    /// 例外：<see cref="QueryChargeLine"/>、<see cref="QueryDraftPage"/> 与 <see cref="GetBillingSummary"/>
    /// 走 <see cref="ConsolCostQuery"/>，因为合单的成本数据在 JobConsolCost 上，
    /// 而不在 BillingCore 读的 JobHeader('JK') → JobCharge / AccTransactionHeader 链路上。
    /// </summary>
    public class ConsolidationBillingApplication : SntBackendApplicationBase, IConsolidationBillingApplication
    {
        private readonly BillingCore _billingCore;
        private readonly IBillingApplication _billingApplication;
        private readonly ConsolCostQuery _consolCostQuery;

        public ConsolidationBillingApplication(BillingCore billingCore, IBillingApplication billingApplication,
            ConsolCostQuery consolCostQuery)
        {
            _billingCore = billingCore;
            _billingApplication = billingApplication;
            _consolCostQuery = consolCostQuery;
        }

        public Task<ConsolBillingCostLineOutput> QueryChargeLine(ConsolBillingChargeLineInput input)
        {
            // JobConsolCost 只有成本侧，AR 无数据来源：返回空结果而不是抛错，让前端 AR 页签显示空。
            if (!string.Equals(input?.chargeType?.Trim(), "AP", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new ConsolBillingCostLineOutput());

            return _consolCostQuery.QueryCostLineAsync(input?.jkPk,
                input?.SkipCount ?? 0, input?.MaxResultCount ?? 20, input?.Sorting);
        }

        public Task<BillingDraftPageOutput> QueryDraftPage(ConsolBillingDraftPageInput input)
        {
            // 合单侧同样绕开 BillingCore：它按 ah_jh -> JobHeader('JK') 找发票，而库里这种作业头
            // 一条都没有，AnchorJobHeaderPksAsync 返回空后直接短路，接口恒返回空列表。
            // 发票只能从成本行的 E6_AH_APInvoice 反查，见 ConsolCostQuery.QueryDraftPageAsync。
            //
            // AR 与 QueryChargeLine 一样无数据来源，返回空让前端 AR 页签显示空；
            // chargeType 留空表示不按账本过滤，而合单侧只可能有 AP，所以与传 AP 等价。
            var chargeType = input?.chargeType?.Trim();
            if (!string.IsNullOrEmpty(chargeType) &&
                !string.Equals(chargeType, "AP", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new BillingDraftPageOutput());

            return _consolCostQuery.QueryDraftPageAsync(input?.jkPk,
                input?.SkipCount ?? 0, input?.MaxResultCount ?? 20, input?.Sorting);
        }

        public async Task<BillingSummaryDto> GetBillingSummary(string jkPk)
        {
            // AP 口径与 QueryChargeLine 的列表一致（JobConsolCost 本位币成本合计）。
            // AR 合单侧无数据来源，固定 0；profits / grossProfitMargin 沿用原公式，本次无业务含义。
            var ap = await _consolCostQuery.GetApSummaryAsync(jkPk);
            const decimal ar = 0m;

            return new BillingSummaryDto
            {
                ar = ar,
                ap = Math.Round(ap, 2),
                profits = Math.Round(ar - ap, 2),
                grossProfitMargin = 0,
                home_currency = ""
            };
        }

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
