using Abp.Threading.BackgroundWorkers;
using Abp.Threading.Timers;
using SntBackend.DomainService.Share;

namespace SntBackend.DomainService.BackgroundWorkers
{
    /// <summary>
    /// 后台工人
    /// </summary>
    public abstract class SntBackendBackgroundWorkerBase : AsyncPeriodicBackgroundWorkerBase
    {
        protected SntBackendBackgroundWorkerBase(AbpAsyncTimer timer)
             : base(timer)
        {
            LocalizationSourceName = SntBackendConsts.LocalizationSourceName;

            // Default value: 5000 (5 seconds). 
            Timer.Period = 5000;
        }
    }
}
