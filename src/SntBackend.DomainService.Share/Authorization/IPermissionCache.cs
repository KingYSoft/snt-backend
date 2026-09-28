using System.Collections.Generic;
using System.Threading.Tasks;

namespace SntBackend.DomainService.Share.Authorization
{
    public interface IPermissionCache
    {
        Task<HashSet<string>> GetGrantedNamesAsync(string staffPk);

        Task ClearAsync(string staffPk);

        Task ClearAllAsync();
    }
}
