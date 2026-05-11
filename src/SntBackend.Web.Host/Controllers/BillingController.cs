using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Billing;
using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Po.Dto;
using SntBackend.Web.Core.Controllers;
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
}
