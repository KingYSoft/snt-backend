using SntBackend.DomainService.Share.App;
using System.Threading.Tasks;

namespace SntBackend.Application.Health
{
    public class HealthApplication : SntBackendApplicationBase, IHealthApplication
    {
        // private readonly IAppOracleRepository _appQueryRepository;
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        // private readonly IAppMySqlRepository _appMySqlRepository;
        public HealthApplication(
            IAppSqlServerRepository appSqlServerRepository)
        {
            _appSqlServerRepository = appSqlServerRepository;
        }

        public async Task<string> Check()
        {
            // oracle sql
            // return await _appQueryRepository.ExecuteScalarAsync<string>("select to_char(sysdate, 'yyyy-mm-dd hh24:mi:ss') from dual");

            // sqlServer sql
            return await _appSqlServerRepository.ExecuteScalarAsync<string>("SELECT CONVERT(varchar(200), GETDATE(),120)");

            // mysql sql
            //return await _appMySqlRepository.ExecuteScalarAsync<string>("SELECT date_format(now(), '%Y-%m-%d %H:%i:%s')");
        }
    }
}
