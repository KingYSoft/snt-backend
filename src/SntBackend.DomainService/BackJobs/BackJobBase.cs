using Abp.BackgroundJobs;
using SntBackend.DomainService.Share;

namespace SntBackend.DomainService.BackJobs
{
    /// <summary>
    /// 后台工作队列
    /// </summary>
    /// <typeparam name="TArgs"></typeparam>
    public abstract class BackJobBase<TArgs> : AsyncBackgroundJob<TArgs>
    {
        protected BackJobBase()
            : base()
        {
            LocalizationSourceName = SntBackendConsts.LocalizationSourceName;
        }
    }
}
