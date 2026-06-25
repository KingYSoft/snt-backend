using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Billing.Dto.MatchTransaction;
using SntBackend.Application.Po.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SntBackend.Application.Billing
{
    public interface IBillingApplication : ISntBackendApplicationBase
    {
        Task<BillingTblOutput> ApTbl(BillingTblInput input);
        Task<BillingTblOutput> ArTbl(BillingTblInput input);
        Task<AccTransactionHeaderDtoOutput> Detail(string id);
        Task<WriteOffTblOutput> WriteOffTbl(WriteOffTblInput input);
        Task<WriteOffDetailOutput> WriteOffDetail(WriteOffDetailInput input);

        Task<OutstandingInvoiceOutput> QueryOutstandingInvoices(OutstandingInvoiceInput input);
        Task<SaveMatchWriteOffOutput> SaveMatchWriteOff(SaveMatchWriteOffInput input);
        Task<MatchTransactionPageOutput> QueryMatchTransactionPage(MatchTransactionPageInput input);
        Task<List<AccTransactionLinesDtoOutput>> QueryMatchTransactionLines(MatchTransactionLinesInput input);
        Task<MatchTransactionDetailOutput> MatchTransactionDetail(string pk);
        Task<List<AccBankAccountDtoOutput>> QueryWriteOffBank(WriteOffBankInput input);

        Task<BillingChargeLineOutput> QueryChargeLine(BillingChargeLineInput input);
        Task<BillingDraftPageOutput> QueryDraftPage(BillingDraftPageInput input);
        Task<BillingSummaryDto> GetBillingSummary(string shpPk);
        Task<QueryChargesByInvoiceOutput> QueryChargesByInvoiceNo(string invoiceNo);
        Task<QueryOrgAddressOutput> QueryOrgAddress(QueryOrgAddressInput input);
        Task<List<CurrencyOptionOutput>> CurrencyOptions(string query);

        /// <summary>费用代码下拉框（来源 AccChargeCode）。</summary>
        Task<List<ChargeCodeOptionOutput>> ChargeCodeOptions(string query);

        /// <summary>
        /// 当前 home/本位币：取第一家启用的 GlbCompany 的本位币(gc_rx_nklocalcurrency)。
        /// 注：snt 登录无 用户→分公司 映射，故按公司维度返回，而非按用户分公司。
        /// </summary>
        Task<string> GetHomeCurrency();

        /// <summary>
        /// 新增 / 修改 应收应付费用（JobCharge）。无 jr_pk 新增，有 jr_pk 修改。
        /// </summary>
        Task<BillingCreateOrUpdateOutput> CreateOrUpdate(BillingCreateInput input);

        /// <summary>
        /// 生成草稿发票：按 结算单位+币种 分组，为选中的 JobCharge 生成 ah_postdate 为空的
        /// AccTransactionHeader + AccTransactionLines，并回填 jr_al_arline / jr_al_apline。
        /// 返回新建的发票号列表。
        /// </summary>
        Task<List<string>> GenerateDraft(GenerateDraftInput input);

        /// <summary>
        /// 过账：把草稿发票头/行的 postdate 置为当前日期，并把关联 JobCharge 的过账状态置为 posted。
        /// 返回过账成功的发票头数量。
        /// </summary>
        Task<int> PostCharge(PostChargeInput input);

        /// <summary>
        /// 批量删除费用（仅未开票，即 jr_al_arline/jr_al_apline 均为空的行做逻辑删除 jr_isvalid=0）。
        /// 返回删除行数。
        /// </summary>
        Task<int> Delete(List<string> jrPks);

        /// <summary>
        /// 作废草稿发票（未过账，ah_postdate IS NULL）：发票头 ah_iscancelled=1，
        /// 并解锁关联 JobCharge（清空 jr_al_*line 与过账状态）。返回作废发票头数量。
        /// </summary>
        Task<int> VoidDraftInvoice(VoidInvoiceInput input);

        /// <summary>
        /// 作废正式账单（已过账）：有核销/付款的不允许作废。发票头 ah_iscancelled=1 并解锁关联 JobCharge。
        /// 入参为发票号 ah_transactionnum 列表。返回作废数量。
        /// </summary>
        Task<int> VoidPostedInvoice(List<string> invoiceNos);

        /// <summary>
        /// 编辑草稿发票：在某张未过账发票上 删除 / 修改 / 新增 费用，并同步发票行与发票头汇总。
        /// 返回受影响的费用行数。
        /// </summary>
        Task<int> EditDraftInvoice(DraftInvoiceEditInput input);
    }
}
