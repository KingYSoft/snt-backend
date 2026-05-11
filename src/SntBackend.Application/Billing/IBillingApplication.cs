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
    }
}
