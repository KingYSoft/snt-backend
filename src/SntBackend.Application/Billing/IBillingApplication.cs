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

        /// <summary>
        /// 按 shipment + AR/AP 分页查询发票头。类型含 INV(发票) 与 CRD(作废已过账账单时生成的冲销单)。
        /// </summary>
        Task<BillingDraftPageOutput> QueryDraftPage(BillingDraftPageInput input);
        Task<BillingSummaryDto> GetBillingSummary(string shpPk);
        Task<QueryChargesByInvoiceOutput> QueryChargesByInvoiceNo(string invoiceNo);
        Task<QueryOrgAddressOutput> QueryOrgAddress(QueryOrgAddressInput input);
        Task<List<CurrencyOptionOutput>> CurrencyOptions(string query);

        /// <summary>费用代码下拉框（来源 AccChargeCode）。</summary>
        Task<List<ChargeCodeOptionOutput>> ChargeCodeOptions(string query);

        /// <summary>分公司/分支下拉框（来源 GlbBranch）。</summary>
        Task<List<BranchOptionOutput>> BranchOptions(string query);

        /// <summary>GST 税率下拉框（来源 AccTaxRate）。</summary>
        Task<List<GstRateOptionOutput>> GstRateOptions(string query);

        /// <summary>WHT 预扣税下拉框（来源 AccWithholding）。</summary>
        Task<List<WhtRateOptionOutput>> WhtRateOptions(string query);

        /// <summary>VAT class 下拉框（来源 AccInvMsg）。</summary>
        Task<List<VatClassOptionOutput>> VatClassOptions(string query);

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
        /// 直接过账：按 结算单位+币种 为选中的 JobCharge 建发票头/行后立即过账
        /// （postdate 置为当前日期，关联 JobCharge 过账状态置 posted），无需预先生成草稿。
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
        /// 作废正式账单（已过账）：有核销/付款的不允许作废。按 snt 库内既有 CRD 的惯例走整套冲销流程 ——
        /// 建一张金额取反的 CRD（AR 取锚点序列新号 / AP 沿用原号，desc 写「撤销相关于 …」）、
        /// 建两条 AccTransactionMatchLink 把原单与 CRD 对冲、两边 outstanding 清零并置 fullypaiddate、
        /// 原单与 CRD 均 ah_iscancelled=1、解锁关联 JobCharge（费用可重新开票）。
        /// </summary>
        /// <param name="invoiceNos">发票号 ah_transactionnum 列表</param>
        /// <param name="reason">3 字原因码（库内取值如 WOR/IAM/IDE），写入 ap_reason 与冲销说明；可空</param>
        /// <param name="reasonDesc">原因说明文字，写入冲销说明；可空</param>
        Task<int> VoidPostedInvoice(List<string> invoiceNos, string reason = null, string reasonDesc = null);

        /// <summary>
        /// 编辑草稿发票：在某张未过账发票上 删除 / 修改 / 新增 费用，并同步发票行与发票头汇总。
        /// 返回受影响的费用行数。
        /// </summary>
        Task<int> EditDraftInvoice(DraftInvoiceEditInput input);

        /// <summary>
        /// 发票打印：按发票号批量生成 PDF，一张发票一个文件。
        /// 返回每张发票的 PDF 相对访问路径。
        /// </summary>
        Task<GenerateInvoicePdfOutput> GenerateInvoicePdf(GenerateInvoicePdfInput input);
    }
}
