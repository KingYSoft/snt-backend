using Abp.Runtime.Session;
using SntBackend.DomainService.Share;

namespace SntBackend.DomainService
{
    public abstract class SntBackendDomainServiceBase : Abp.Domain.Services.DomainService,
        ISntBackendDomainServiceBase
    {
        public IAbpSession AbpSession { get; set; }
        protected SntBackendDomainServiceBase()
            : base()
        {
            LocalizationSourceName = SntBackendConsts.LocalizationSourceName;
            AbpSession = NullAbpSession.Instance;
        }
    }
}
