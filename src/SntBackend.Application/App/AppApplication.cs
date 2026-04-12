using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Abp.Runtime.Caching;
using Dapper;
using SntBackend.DomainService.Share.App;

namespace SntBackend.Application.App
{
    public class AppApplication : SntBackendApplicationBase, IAppApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        private readonly ICacheManager _cacheManager;
        public AppApplication(IAppSqlServerRepository appSqlServerRepository,
                ICacheManager cacheManager
        )
        {
            _appSqlServerRepository = appSqlServerRepository;
            _cacheManager = cacheManager;
        }
    }
}