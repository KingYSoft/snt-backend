using Abp.Domain.Entities;
using Abp.EntityFrameworkCore;
using Abp.EntityFrameworkCore.Repositories;

namespace SntBackend.SqlServer.EntityFrameworkCore.Repositories
{
    /// <summary>
    /// EF Core Repository base
    /// efcore 仓储基类
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    /// <typeparam name="TPrimaryKey">Primary key type of the entity</typeparam>
    public class SntBackendSqlServerEfCoreRepositoryBase<TEntity, TPrimaryKey> : EfCoreRepositoryBase<SntBackendSqlServerDbContext, TEntity, TPrimaryKey>
        where TEntity : class, IEntity<TPrimaryKey>
    {
        protected SntBackendSqlServerEfCoreRepositoryBase(IDbContextProvider<SntBackendSqlServerDbContext> dbContextProvider)
            : base(dbContextProvider)
        {
        }

        // Add your common methods for all repositories
    }
}
