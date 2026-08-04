using System;
using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Consolidation;
using SntBackend.Application.Consolidation.Dto;
using SntBackend.Web.Core.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Facade.AspNetCore.Mvc.Authorization;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// 合单(Consolidation)应收应付账单控制器。
/// 路由与 /billing/* 一一对应，锚点从运单换成合单（入参 jkPk = JobConsol.jk_pk）。
/// 下拉框类接口（charge-code-options / branch-options / gst-rate-options / wht-rate-options /
/// vat-class-options / home-currency）与锚点无关，合单页继续用 /billing/* 那几个。
/// </summary>
[Route("consolidation/billing")]
public class ConsolidationBillingController : SntBackendControllerBase
{
    private readonly IConsolidationBillingApplication _consolidationBillingApplication;

    public ConsolidationBillingController(IConsolidationBillingApplication consolidationBillingApplication)
    {
        _consolidationBillingApplication = consolidationBillingApplication;
    }

    /// <summary>
    /// 按合单 + AR/AP 分页查询费用行（JobCharge）
    /// </summary>
    [HttpPost]
    [Route("charge-line")]
    [NoToken]
    public async Task<JsonResponse<BillingChargeLineOutput>> QueryChargeLine([FromBody] ConsolBillingChargeLineInput input)
    {
        var result = await _consolidationBillingApplication.QueryChargeLine(input);
        return new JsonResponse<BillingChargeLineOutput> { Data = result };
    }

    /// <summary>
    /// 按合单 + AR/AP 分页查询发票头（含作废后生成的 CRD 冲销单）
    /// </summary>
    [HttpPost]
    [Route("draft-page")]
    [NoToken]
    public async Task<JsonResponse<BillingDraftPageOutput>> QueryDraftPage([FromBody] ConsolBillingDraftPageInput input)
    {
        var result = await _consolidationBillingApplication.QueryDraftPage(input);
        return new JsonResponse<BillingDraftPageOutput> { Data = result };
    }

    /// <summary>
    /// 合单账单汇总（毛利率、AR、AP、利润）
    /// </summary>
    [HttpGet]
    [Route("summary")]
    [NoToken]
    public async Task<JsonResponse<BillingSummaryDto>> GetBillingSummary([FromQuery] string jkPk)
    {
        var result = await _consolidationBillingApplication.GetBillingSummary(jkPk);
        return new JsonResponse<BillingSummaryDto> { Data = result };
    }

    /// <summary>
    /// 新增 / 修改合单应收应付费用（JobCharge）
    /// </summary>
    [HttpPost]
    [Route("create-or-update")]
    [NoToken]
    public async Task<JsonResponse<BillingCreateOrUpdateOutput>> CreateOrUpdate([FromBody] ConsolBillingCreateInput input)
    {
        var result = await _consolidationBillingApplication.CreateOrUpdate(input);
        return new JsonResponse<BillingCreateOrUpdateOutput> { Data = result };
    }

    /// <summary>
    /// 生成草稿发票（返回新建的发票号列表）
    /// </summary>
    [HttpPost]
    [Route("generate-draft")]
    [NoToken]
    public async Task<JsonResponse<List<string>>> GenerateDraft([FromBody] GenerateDraftInput input)
    {
        var result = await _consolidationBillingApplication.GenerateDraft(input);
        return new JsonResponse<List<string>> { Data = result };
    }

    /// <summary>
    /// 过账（返回过账成功的发票头数量）
    /// </summary>
    [HttpPost]
    [Route("post-charge")]
    [NoToken]
    public async Task<JsonResponse<int>> PostCharge([FromBody] PostChargeInput input)
    {
        try
        {
            var result = await _consolidationBillingApplication.PostCharge(input);
            return new JsonResponse<int> { Data = result };
        }
        catch (Exception ex)
        {
            // 过账失败：把错误信息返回前端
            return new JsonResponse<int>(false, ex.Message);
        }
    }

    /// <summary>
    /// 批量删除费用（仅未开票行）
    /// </summary>
    [HttpPost]
    [Route("delete")]
    [NoToken]
    public async Task<JsonResponse<int>> Delete([FromBody] List<string> jrPks)
    {
        var result = await _consolidationBillingApplication.Delete(jrPks);
        return new JsonResponse<int> { Data = result };
    }

    /// <summary>
    /// 作废草稿发票（未过账）
    /// </summary>
    [HttpPost]
    [Route("void-draft")]
    [NoToken]
    public async Task<JsonResponse<int>> VoidDraftInvoice([FromBody] VoidInvoiceInput input)
    {
        var result = await _consolidationBillingApplication.VoidDraftInvoice(input);
        return new JsonResponse<int> { Data = result };
    }

    /// <summary>
    /// 作废正式账单（已过账，按发票号）。作废会建一张金额取反的 CRD 冲销单并与原单对冲。
    /// </summary>
    /// <param name="invoiceNos">发票号列表</param>
    /// <param name="reason">可选，3 字原因码（如 WOR/IAM/IDE），写入冲销说明与核销记录</param>
    /// <param name="reasonDesc">可选，原因说明文字，写入冲销说明</param>
    [HttpPost]
    [Route("void-posted")]
    [NoToken]
    public async Task<JsonResponse<int>> VoidPostedInvoice(
        [FromBody] List<string> invoiceNos,
        [FromQuery] string reason = null,
        [FromQuery] string reasonDesc = null)
    {
        try
        {
            var result = await _consolidationBillingApplication.VoidPostedInvoice(invoiceNos, reason, reasonDesc);
            return new JsonResponse<int> { Data = result };
        }
        catch (Exception ex)
        {
            return new JsonResponse<int>(false, ex.Message);
        }
    }

    /// <summary>
    /// 编辑草稿发票（删除/修改/新增费用）
    /// </summary>
    [HttpPost]
    [Route("edit-draft")]
    [NoToken]
    public async Task<JsonResponse<int>> EditDraftInvoice([FromBody] DraftInvoiceEditInput input)
    {
        var result = await _consolidationBillingApplication.EditDraftInvoice(input);
        return new JsonResponse<int> { Data = result };
    }

    /// <summary>
    /// 按发票号查关联的费用明细
    /// </summary>
    [HttpGet]
    [Route("charges-by-invoice")]
    [NoToken]
    public async Task<JsonResponse<QueryChargesByInvoiceOutput>> QueryChargesByInvoiceNo([FromQuery] string invoiceNo)
    {
        var result = await _consolidationBillingApplication.QueryChargesByInvoiceNo(invoiceNo);
        return new JsonResponse<QueryChargesByInvoiceOutput> { Data = result };
    }

    /// <summary>
    /// 发票打印：按发票号批量生成 PDF，返回每张发票的 PDF 相对访问路径
    /// </summary>
    [HttpPost]
    [Route("invoice/pdf")]
    [NoToken]
    public async Task<JsonResponse<GenerateInvoicePdfOutput>> GenerateInvoicePdf([FromBody] GenerateInvoicePdfInput input)
    {
        var result = await _consolidationBillingApplication.GenerateInvoicePdf(input);
        return new JsonResponse<GenerateInvoicePdfOutput> { Data = result };
    }
}
