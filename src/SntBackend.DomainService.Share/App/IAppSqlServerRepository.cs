using Abp.Dependency;
using Facade.Dapper.SqlServer;

namespace SntBackend.DomainService.Share.App
{
    public interface IAppSqlServerRepository : ISqlServerQueryRepository, ITransientDependency
    {
    }
}
