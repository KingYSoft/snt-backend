using Facade.Dapper;
using Facade.Dapper.SqlServer;
using SntBackend.DomainService.Share.App;

namespace SntBackend.SqlServer.EntityFrameworkCore.Repositories
{
    public class AppSqlServerRepository : SqlServerQueryRepository<SntBackendSqlServerDbContext>, IAppSqlServerRepository
    {
        public AppSqlServerRepository(IFacadeConnectionProvider facadeConnectionProvider)
            : base(facadeConnectionProvider)
        {
        }
    } 
}
