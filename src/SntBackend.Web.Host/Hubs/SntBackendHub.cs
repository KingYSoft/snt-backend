using Abp.RealTime;
using SntBackend.Web.Core.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Hubs
{
    public class SntBackendHub : SntBackendHubCore
    {
        public SntBackendHub(IOnlineClientManager onlineClientManager, IOnlineClientInfoProvider onlineClientInfoProvider)
            : base(onlineClientManager, onlineClientInfoProvider)
        {
        }
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

    }
}
