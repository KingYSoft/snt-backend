using Abp.Dependency;
using Abp.TestBase;
using Castle.Core.Logging;

namespace SntBackend.Tests
{
    public abstract class SntBackendTestBase : AbpIntegratedTestBase<SntBackendTestModule>
    {
        public ILogger Logger { get; set; }
        protected SntBackendTestBase()
            : base(true, IocManager.Instance)
        {
            AbpSession.TenantId = 1;
            AbpSession.UserId = 10000;
            Logger = Resolve<ILogger>();
        }
    }
}
