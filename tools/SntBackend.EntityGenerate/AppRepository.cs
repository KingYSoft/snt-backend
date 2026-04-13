using Facade.Dapper;
using Facade.Dapper.SqlServer;

namespace SntBackend.EntityGenerate
{
    public class AppRepository : SqlServerQueryRepository, IAppRepository
    {
        public AppRepository(IFacadeConnectionProvider facadeConnectionProvider)
            : base(facadeConnectionProvider)
        {
        }
    }
}
