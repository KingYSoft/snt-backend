using System;
using System.Data;
using Abp.Modules;
using Abp.Reflection.Extensions;
using Dapper;
using Facade.Dapper.SqlServer;
using Facade.NLogger; 

namespace SntBackend.DomainService.Share
{
    [DependsOn(
           typeof(FacadeNLoggerModule), 
           typeof(FacadeDapperSqlServerModule)
           )]
    public class SntBackendDomainServiceShareModule : AbpModule
    {

        public SntBackendDomainServiceShareModule()
        {
        }
        public override void PreInitialize()
        { 
            SqlMapper.AddTypeHandler(new GuidToStringHandler());
        }

        public override void Initialize()
        {
            IocManager.RegisterAssemblyByConvention(typeof(SntBackendDomainServiceShareModule).GetAssembly());
        }

    }
    
    public class GuidToStringHandler : SqlMapper.TypeHandler<string>
    {
        public override void SetValue(IDbDataParameter parameter, string value)
        {
            parameter.Value = Guid.Parse(value);
        }

        public override string Parse(object value)
        {
            return value.ToString();
        }
    }
}