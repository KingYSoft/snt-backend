using Facade;
using Abp.Dependency;
using SntBackend.DomainService.Share.Authorization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SntBackend.Web.Core.Authorization
{
    public class SystemPermissionChecker : ITransientDependency
    {
        private readonly IPermissionCache _permissionCache;

        public SystemPermissionChecker(IPermissionCache permissionCache)
        {
            _permissionCache = permissionCache;
        }

        public async Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
        {
            var staffPk = principal?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            if (string.IsNullOrWhiteSpace(staffPk) || string.IsNullOrWhiteSpace(permissionName))
                return false;

            var names = await _permissionCache.GetGrantedNamesAsync(staffPk);
            return names.Contains("*") || names.Contains(permissionName);
        }

        public async Task EnsureGrantedAsync(ClaimsPrincipal principal, string permissionName)
        {
            if (!await IsGrantedAsync(principal, permissionName))
                throw new AppException("You do not have permission to access this system setting.");
        }
    }
}
