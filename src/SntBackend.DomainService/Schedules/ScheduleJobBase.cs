using Abp.Quartz;
using Facade.Quartz;
using SntBackend.DomainService.Share;

namespace SntBackend.DomainService.Schedules
{
    /// <summary>
    /// 任务调度作业
    /// </summary>
    public abstract class ScheduleJobBase : FacadeScheduleJobBase
    {
        protected ScheduleJobBase() : base()
        {
            LocalizationSourceName = SntBackendConsts.LocalizationSourceName;
        }
    }
}
