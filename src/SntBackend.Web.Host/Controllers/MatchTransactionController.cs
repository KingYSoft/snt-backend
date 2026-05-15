using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Billing;
using SntBackend.Application.Billing.Dto.MatchTransaction;
using SntBackend.Application.Po.Dto;
using SntBackend.Web.Core.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Facade.AspNetCore.Mvc.Authorization;

namespace SntBackend.Web.Host.Controllers;

/// <summary>
/// 核销管理控制器
/// </summary>
[Route("match-transactions")]
public class MatchTransactionController : SntBackendControllerBase
{
    private readonly IBillingApplication _billingApplication;

    public MatchTransactionController(IBillingApplication billingApplication)
    {
        _billingApplication = billingApplication;
    }

    /// <summary>
    /// 查询可核销的未结发票
    /// </summary>
    [HttpPost]
    [Route("query-outstandingInvoices")] 
    public async Task<JsonResponse<OutstandingInvoiceOutput>> QueryOutstandingInvoices([FromBody] OutstandingInvoiceInput input)
    {
        var result = await _billingApplication.QueryOutstandingInvoices(input);
        return new JsonResponse<OutstandingInvoiceOutput> { Data = result };
    }

    /// <summary>
    /// 保存核销
    /// </summary>
    [HttpPost]
    [Route("save-matchWriteOff")]
    public async Task<JsonResponse<SaveMatchWriteOffOutput>> SaveMatchWriteOff([FromBody] SaveMatchWriteOffInput input)
    {
        var result = await _billingApplication.SaveMatchWriteOff(input);
        return new JsonResponse<SaveMatchWriteOffOutput> { Data = result };
    }

    /// <summary>
    /// 核销记录分页查询
    /// </summary>
    [HttpGet]
    [Route("query-page")] 

    public async Task<JsonResponse<MatchTransactionPageOutput>> QueryPage([FromQuery] MatchTransactionPageInput input)
    {
        var result = await _billingApplication.QueryMatchTransactionPage(input);
        return new JsonResponse<MatchTransactionPageOutput> { Data = result };
    }

    /// <summary>
    /// 核销记录明细行
    /// </summary>
    [HttpPost]
    [Route("query-lines")]
    public async Task<JsonResponse<List<AccTransactionLinesDtoOutput>>> QueryLines([FromBody] MatchTransactionLinesInput input)
    {
        var result = await _billingApplication.QueryMatchTransactionLines(input);
        return new JsonResponse<List<AccTransactionLinesDtoOutput>> { Data = result };
    }

    /// <summary>
    /// 核销记录完整详情
    /// </summary>
    [HttpGet]
    [Route("detail")] 
    public async Task<JsonResponse<MatchTransactionDetailOutput>> Detail([FromQuery] string Pk)
    {
        var result = await _billingApplication.MatchTransactionDetail(Pk);
        if (result == null)
        {
            return new JsonResponse<MatchTransactionDetailOutput>(false, "Match transaction record not found.");
        }
        return new JsonResponse<MatchTransactionDetailOutput> { Data = result };
    }

    /// <summary>
    /// 查询核销可用银行账户
    /// </summary>
    [HttpPost]
    [Route("get-writeOff-bank")]
    public async Task<JsonResponse<List<AccBankAccountDtoOutput>>> GetWriteOffBank([FromBody] WriteOffBankInput input)
    {
        var result = await _billingApplication.QueryWriteOffBank(input);
        return new JsonResponse<List<AccBankAccountDtoOutput>> { Data = result };
    }

    /// <summary>
    /// 查询结算公司（未结发票关联的公司）
    /// </summary>
    [HttpGet]
    [Route("query-org-address")]
    [NoToken]
    public async Task<JsonResponse<QueryOrgAddressOutput>> QueryOrgAddress([FromQuery] QueryOrgAddressInput input)
    {
        var result = await _billingApplication.QueryOrgAddress(input);
        return new JsonResponse<QueryOrgAddressOutput> { Data = result };
    }

    /// <summary>
    /// 币种下拉框
    /// </summary>
    [HttpGet]
    [Route("currency-options")]
    public async Task<JsonResponse<List<CurrencyOptionOutput>>> CurrencyOptions([FromQuery] string query)
    {
        var result = await _billingApplication.CurrencyOptions(query);
        return new JsonResponse<List<CurrencyOptionOutput>> { Data = result };
    }
}
