using Abp.Dependency;
using Abp.Runtime.Caching;
using Dapper;
using SntBackend.DomainService.Share;
using SntBackend.DomainService.Share.App;
using SntBackend.DomainService.Share.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Authorization
{
    public sealed class PermissionCache : IPermissionCache, ITransientDependency
    {
        private const string CacheName = "SntGrantedPermissions";
        private readonly IAppSqlServerRepository _repository;
        private readonly ICacheManager _cacheManager;

        public PermissionCache(IAppSqlServerRepository repository, ICacheManager cacheManager)
        {
            _repository = repository;
            _cacheManager = cacheManager;
        }

        private ITypedCache<string, HashSet<string>> Cache =>
            _cacheManager.GetCache<string, HashSet<string>>(CacheName);

        public async Task<HashSet<string>> GetGrantedNamesAsync(string staffPk)
        {
            if (string.IsNullOrWhiteSpace(staffPk))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Cache.DefaultSlidingExpireTime = TimeSpan.FromMinutes(5);
            return await Cache.GetAsync(staffPk, async () =>
            {
                var rows = await _repository.QueryAsync<PermissionRow>(@"
SELECT DISTINCT pn.permission_name, g.is_admin
FROM SYS_GROUP_USER gu
INNER JOIN GlbStaff s
    ON s.gs_pk = gu.user_pk
   AND s.gs_isactive = 1
   AND s.gs_isvalid = 1
   AND s.gs_canlogin = 1
INNER JOIN SYS_GROUP g
    ON g.pk = gu.group_pk
   AND g.is_active = 1
LEFT JOIN SYS_GROUP_PERMISSION p
    ON p.group_pk = g.pk
   AND p.is_allow = N'Y'
LEFT JOIN SYS_GROUP_PERMISSION_NAME pn
    ON pn.group_permission_pk = p.pk
WHERE gu.user_pk = @staffPk", new { staffPk });

                var names = new HashSet<string>(
                    rows.Where(x => !string.IsNullOrWhiteSpace(x.permission_name))
                        .Select(x => x.permission_name.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                if (rows.Any(x => string.Equals(x.is_admin, "Y", StringComparison.OrdinalIgnoreCase)))
                    names.Add("*");

                return names;
            });
        }

        public Task ClearAsync(string staffPk)
        {
            return string.IsNullOrWhiteSpace(staffPk)
                ? Task.CompletedTask
                : Cache.RemoveAsync(staffPk);
        }

        public Task ClearAllAsync()
        {
            Cache.Clear();
            return Task.CompletedTask;
        }

        private sealed class PermissionRow
        {
            public string permission_name { get; set; }
            public string is_admin { get; set; }
        }
    }
}
