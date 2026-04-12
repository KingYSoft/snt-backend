using Abp.Runtime.Caching;
using Abp.Web.Models.AbpUserConfiguration;
using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using SntBackend.Application.App;
using SntBackend.Web.Core.Controllers;
using SntBackend.Web.Host.Configuration;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Abp.Auditing;
using SntBackend.Web.Core.Filters;

namespace SntBackend.Web.Host.Controllers
{
    /// <summary>
    /// app
    /// </summary>
    [Route("app")]
    public class AppController : SntBackendControllerBase
    {

        private readonly IAppApplication _appApplication;
        private readonly MyUserConfigurationBuilder _appUserConfigurationBuilder;
        private readonly ICacheManager _cacheManager;
        // private readonly ISessionManager _sessionManager;
        public AppController(IAppApplication appApplication,
            MyUserConfigurationBuilder appUserConfigurationBuilder, ICacheManager cacheManager
            )
        {
            _appApplication = appApplication;
            _appUserConfigurationBuilder = appUserConfigurationBuilder;
            _cacheManager = cacheManager;
            // _sessionManager = sessionManager;
        }

        [Route("all/config")]
        [HttpGet]
        [NoToken]
        [DisableAuditing]
        [Debounce]
        public async Task<JsonResponse<AbpUserConfigurationDto>> AllConfig()
        {
            var user_id = AbpSession.UserId ?? 0;
            var d = await _appUserConfigurationBuilder.GetAll();
            d.Custom.TryAdd("defaultSourceName", LocalizationSourceName);
            // var userSession = await _sessionManager.UserSession();
            // d.Custom.TryAdd("userSession", userSession);
            // var comboboxConfig = await _appApplication.ComboboxConfig();
            // d.Custom.TryAdd("appComboboxConfig", comboboxConfig);

            return new JsonResponse<AbpUserConfigurationDto>()
            {
                Data = d
            };
        }
        [Route("cache/clear")]
        [HttpGet]
        [NoToken]
        public async Task<JsonResponse> CacheClear()
        {
            var aa = _cacheManager.GetAllCaches();
            foreach (var a in aa)
            {
                await a.ClearAsync();
            }
            return new JsonResponse();
        }
    }
}
