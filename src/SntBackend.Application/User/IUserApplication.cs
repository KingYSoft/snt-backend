using Abp.Application.Services.Dto;
using SntBackend.Application.SystemSettings.Dto;
using System.Threading.Tasks;

namespace SntBackend.Application.User
{
    public interface IUserApplication : ISntBackendApplicationBase
    {
        Task<PagedResultDto<UserDtoOutput>> QueryPage(UserQueryInput input);
        Task<UserDtoOutput> Save(UserSaveInput input);
        Task Delete(string pk);
        Task<UserDtoOutput> Detail(string pk);
        Task<UserQueryAllOutput> QueryAll(string query);
    }
}
