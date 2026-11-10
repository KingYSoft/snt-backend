using SntBackend.Application.Settlement.ClosingPeriod.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;
using Abp.Application.Services.Dto;

namespace SntBackend.Application.Settlement.ClosingPeriod
{
    public interface IClosingPeriodApplication : ISntBackendApplicationBase
    {
        Task<PagedResultDto<ClosingPeriodRecord>> QueryPage(ClosingPeriodQueryInput input);
        Task<List<ClosingPeriodRecord>> Create(ClosingPeriodSaveInput input);
        Task<ClosingPeriodRecord> Update(ClosingPeriodUpdateInput input);
    }
}
