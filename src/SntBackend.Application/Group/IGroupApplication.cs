using SntBackend.Application.SystemSettings.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.Group
{
    public interface IGroupApplication : ISntBackendApplicationBase
    {
        Task<GroupAllPermissionOutput> AllPermission();
        Task<Abp.Application.Services.Dto.PagedResultDto<GroupDtoOutput>> QueryPage(GroupQueryInput input);
        Task<GroupDetailOutput> Detail(string pk);
        Task<GroupSaveOutput> Save(GroupSaveInput input);
        Task<CompanyBranchDeptOptionsOutput> QueryCompanyBranchDeptOptions();
    }
}
