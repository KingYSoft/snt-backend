using Abp.Application.Services;
using Abp.Application.Services.Dto;
using Abp.Domain.Entities;
using Facade.Core.Web;
using SntBackend.DomainService.Share;

namespace SntBackend.Application
{
    public abstract class SntBackendApplicationBase : ApplicationService, ISntBackendApplicationBase
    {
        protected SntBackendApplicationBase()
        {
            LocalizationSourceName = SntBackendConsts.LocalizationSourceName;
        }
    }
}


