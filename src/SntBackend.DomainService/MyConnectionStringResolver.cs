using Abp.Configuration.Startup;
using Abp.Domain.Uow;

namespace SntBackend.DomainService.Share
{
    public class MyConnectionStringResolver : DefaultConnectionStringResolver
    {
        private readonly IFacadeConfiguration _facadeConfiguration;

        public MyConnectionStringResolver(IAbpStartupConfiguration configuration, IFacadeConfiguration facadeConfiguration)
            : base(configuration)
        {
            _facadeConfiguration = facadeConfiguration;
        }

        public override string GetNameOrConnectionString(ConnectionStringResolveArgs args)
        {
            // if (args["DbContextConcreteType"] as Type == typeof(SntBackendSqlServerDbContext))
            // {
            //     return _facadeConfiguration.SqlServerConnString;
            // }
            // else if (args["DbContextConcreteType"] as Type == typeof(SntBackendMySqlDbContext))
            // {
            //     return _facadeConfiguration.MySqlConnString;
            // }

            // default oracle
            return base.GetNameOrConnectionString(args);
        }
    }
}
