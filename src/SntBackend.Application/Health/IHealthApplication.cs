using System;
using System.Threading.Tasks;

namespace SntBackend.Application.Health
{
    public interface IHealthApplication : ISntBackendApplicationBase
    {
        Task<string> Check();
    }
}
