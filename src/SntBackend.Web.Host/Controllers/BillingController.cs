using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Billing;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Billing.Dto.MatchTransaction;
using SntBackend.Application.Po.Dto;
using SntBackend.Web.Core.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Facade.AspNetCore.Mvc.Authorization;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// 应收应付账单前端控制器
/// </summary>
[Route("billing")]
public class BillingController : SntBackendControllerBase
{
    private readonly IBillingApplication _billingApplication;

    public BillingController(IBillingApplication billingApplication)
    {
        _billingApplication = billingApplication;
    }

    /// <summary>
    /// AP 应付账单分页查询
    /// </summary>
    [HttpPost]
    [Route("ap/tbl")]
    [NoToken]
    public async Task<JsonResponse<BillingTblOutput>> ApTbl([FromBody] BillingTblInput input)
    {
        var result = await _billingApplication.ApTbl(input);
        return new JsonResponse<BillingTblOutput> { Data = result };
    }

    /// <summary>
    /// AR 应收账单分页查询
    /// </summary>
    [HttpPost]
    [Route("ar/tbl")]
    [NoToken]
    public async Task<JsonResponse<BillingTblOutput>> ArTbl([FromBody] BillingTblInput input)
    {
        var result = await _billingApplication.ArTbl(input);
        return new JsonResponse<BillingTblOutput> { Data = result };
    }

    /// <summary>
    /// 账单详情
    /// </summary>
    [HttpGet]
    [Route("detail")]
    [NoToken]
    public async Task<JsonResponse<AccTransactionHeaderDtoOutput>> Detail([FromQuery] string id)
    {
        var result = await _billingApplication.Detail(id);
        if (result == null)
        {
            return new JsonResponse<AccTransactionHeaderDtoOutput>(false, "Transaction not found.");
        }
        return new JsonResponse<AccTransactionHeaderDtoOutput> { Data = result };
    }

    /// <summary>
    /// 按 shipment + AR/AP 分页查询费用行（JobCharge）
    /// </summary>
    [HttpPost]
    [Route("charge-line")]
    [NoToken]
    public async Task<JsonResponse<BillingChargeLineOutput>> QueryChargeLine([FromBody] BillingChargeLineInput input)
    {
        var result = await _billingApplication.QueryChargeLine(input);
        return new JsonResponse<BillingChargeLineOutput> { Data = result };
    }

    /// <summary>
    /// 费用代码下拉框（来源 AccChargeCode）
    /// </summary>
    [HttpGet]
    [Route("charge-code-options")]
    public async Task<JsonResponse<List<ChargeCodeOptionOutput>>> ChargeCodeOptions([FromQuery] string query)
    {
        var result = await _billingApplication.ChargeCodeOptions(query);
        return new JsonResponse<List<ChargeCodeOptionOutput>> { Data = result };
    }

    /// <summary>
    /// 分公司/分支下拉框（来源 GlbBranch）
    /// </summary>
    [HttpGet]
    [Route("branch-options")]
    public async Task<JsonResponse<List<BranchOptionOutput>>> BranchOptions([FromQuery] string query)
    {
        var result = await _billingApplication.BranchOptions(query);
        return new JsonResponse<List<BranchOptionOutput>> { Data = result };
    }

    /// <summary>
    /// 当前 home/本位币（取第一家启用公司的 gc_rx_nklocalcurrency）
    /// </summary>
    [HttpGet]
    [Route("home-currency")]
    public async Task<JsonResponse<string>> HomeCurrency()
    {
        var result = await _billingApplication.GetHomeCurrency();
        return new JsonResponse<string> { Data = result };
    }

    /// <summary>
    /// 按 shipment + AR/AP 分页查询发票头（snt 无草稿，统一返回已过账）
    /// </summary>
    [HttpPost]
    [Route("draft-page")]
    [NoToken]
    public async Task<JsonResponse<BillingDraftPageOutput>> QueryDraftPage([FromBody] BillingDraftPageInput input)
    {
        var result = await _billingApplication.QueryDraftPage(input);
        return new JsonResponse<BillingDraftPageOutput> { Data = result };
    }

    /// <summary>
    /// 账单汇总（毛利率、AR、AP、利润）
    /// </summary>
    [HttpGet]
    [Route("summary")]
    [NoToken]
    public async Task<JsonResponse<BillingSummaryDto>> GetBillingSummary([FromQuery] string shpPk)
    {
        var result = await _billingApplication.GetBillingSummary(shpPk);
        return new JsonResponse<BillingSummaryDto> { Data = result };
    }

    /// <summary>
    /// 按发票号查关联的费用明细
    /// </summary>
    [HttpGet]
    [Route("charges-by-invoice")]
    [NoToken]
    public async Task<JsonResponse<QueryChargesByInvoiceOutput>> QueryChargesByInvoiceNo([FromQuery] string invoiceNo)
    {
        var result = await _billingApplication.QueryChargesByInvoiceNo(invoiceNo);
        return new JsonResponse<QueryChargesByInvoiceOutput> { Data = result };
    }

    /// <summary>
    /// 新增 / 修改 应收应付费用（JobCharge）
    /// </summary>
    [HttpPost]
    [Route("create-or-update")]
    [NoToken]
    public async Task<JsonResponse<BillingCreateOrUpdateOutput>> CreateOrUpdate([FromBody] BillingCreateInput input)
    {
        var result = await _billingApplication.CreateOrUpdate(input);
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
        var result = await _billingApplication.GenerateDraft(input);
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
        var result = await _billingApplication.PostCharge(input);
        return new JsonResponse<int> { Data = result };
    }

    /// <summary>
    /// 批量删除费用（仅未开票行）
    /// </summary>
    [HttpPost]
    [Route("delete")]
    [NoToken]
    public async Task<JsonResponse<int>> Delete([FromBody] List<string> jrPks)
    {
        var result = await _billingApplication.Delete(jrPks);
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
        var result = await _billingApplication.VoidDraftInvoice(input);
        return new JsonResponse<int> { Data = result };
    }

    /// <summary>
    /// 作废正式账单（已过账，按发票号）
    /// </summary>
    [HttpPost]
    [Route("void-posted")]
    [NoToken]
    public async Task<JsonResponse<int>> VoidPostedInvoice([FromBody] List<string> invoiceNos)
    {
        var result = await _billingApplication.VoidPostedInvoice(invoiceNos);
        return new JsonResponse<int> { Data = result };
    }

    /// <summary>
    /// 编辑草稿发票（删除/修改/新增费用）
    /// </summary>
    [HttpPost]
    [Route("edit-draft")]
    [NoToken]
    public async Task<JsonResponse<int>> EditDraftInvoice([FromBody] DraftInvoiceEditInput input)
    {
        var result = await _billingApplication.EditDraftInvoice(input);
        return new JsonResponse<int> { Data = result };
    }
}
