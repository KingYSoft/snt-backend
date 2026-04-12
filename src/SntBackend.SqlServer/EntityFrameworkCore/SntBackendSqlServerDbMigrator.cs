using Abp.Dependency;
using Abp.Domain.Uow;
using Abp.EntityFrameworkCore;
using SntBackend.DomainService.Share;
using Microsoft.EntityFrameworkCore;
using System.Transactions;

namespace SntBackend.SqlServer.EntityFrameworkCore
{
    public class SntBackendSqlServerDbMigrator : ITransientDependency
    {
        private readonly IUnitOfWorkManager _unitOfWorkManager;
        private readonly IDbContextResolver _dbContextResolver;
        private readonly IFacadeConfiguration _facadeConfiguration;

        public SntBackendSqlServerDbMigrator(IUnitOfWorkManager unitOfWorkManager, IDbContextResolver dbContextResolver,
            IFacadeConfiguration facadeConfiguration)
        {
            _unitOfWorkManager = unitOfWorkManager;
            _dbContextResolver = dbContextResolver;
            _facadeConfiguration = facadeConfiguration;
        }
        public virtual void CreateOrMigrate()
        {
            using (var uow = _unitOfWorkManager.Begin(TransactionScopeOption.Suppress))
            {
                //using (var dbContext = _unitOfWorkManager.Current.GetDbContext<TDbContext>(MultiTenancySides.Host))
                using (var dbContext = _dbContextResolver.Resolve<SntBackendSqlServerDbContext>(_facadeConfiguration.SqlServerConnString, null))
                {
                    dbContext.Database.Migrate();
                    _unitOfWorkManager.Current.SaveChanges();
                    uow.Complete();
                }
            }
        }
    }
}
