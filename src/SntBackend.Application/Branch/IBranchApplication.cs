using Abp.Application.Services.Dto;
using SntBackend.Application.SystemSettings.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.Branch
{
    public interface IBranchApplication : ISntBackendApplicationBase
    {
        Task<PagedResultDto<BranchDtoOutput>> QueryPage(BranchQueryInput input);
        Task<BranchDtoOutput> Save(BranchSaveInput input);
        Task Delete(string pk);
        Task<BranchDtoOutput> GetByPk(BranchGetInput input);
        Task<BranchGroupListOutput> GroupList();
    }
}
