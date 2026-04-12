using Abp.AspNetCore.SignalR.Hubs;
using Abp.RealTime;
using SntBackend.DomainService.Share;

namespace SntBackend.Web.Core.Hubs
{
    public class SntBackendHubCore : AbpCommonHub
    {
        private readonly IOnlineClientManager _onlineClientManager;
        public SntBackendHubCore(IOnlineClientManager onlineClientManager, IOnlineClientInfoProvider onlineClientInfoProvider)
            : base(onlineClientManager, onlineClientInfoProvider)
        {
            LocalizationSourceName = SntBackendConsts.ConnectionStringName;
            _onlineClientManager = onlineClientManager;
        }
    }
}