using SntBackend.Application.Consolidation.Dto;
using SntBackend.Application.Po.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.Consolidation
{
    public interface IConsolidationApplication : ISntBackendApplicationBase
    {
        Task<ConsolidationTblOutput> Tbl(ConsolidationTblInput input);
        Task<JobConsolDtoOutput> Detail(string id);
    }
}
