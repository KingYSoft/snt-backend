using SntBackend.Application.Billing.Dto;
using SntBackend.Application.Po.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.Billing
{
    public interface IBillingApplication : ISntBackendApplicationBase
    {
        Task<BillingTblOutput> ApTbl(BillingTblInput input);
        Task<BillingTblOutput> ArTbl(BillingTblInput input);
        Task<AccTransactionHeaderDtoOutput> Detail(string id);
        Task<WriteOffTblOutput> WriteOffTbl(WriteOffTblInput input);
        Task<WriteOffDetailOutput> WriteOffDetail(string id);
    }
}
