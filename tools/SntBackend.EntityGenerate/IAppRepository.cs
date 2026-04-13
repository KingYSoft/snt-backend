using Abp.Dependency;
using Facade.Dapper.SqlServer;

namespace SntBackend.EntityGenerate
{
    public interface IAppRepository : ISqlServerQueryRepository, ITransientDependency
    {
    }
}
