using Abp.Dependency;
using Abp.Runtime.Session;
using Dapper;
using Facade;
using SntBackend.DomainService.Share.App;
using SntBackend.DomainService.Share.Authorization;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Excel
{
    public sealed class CurrentCompanyNameProvider : ITransientDependency
    {
        private readonly IAppSqlServerRepository _repository;
        private readonly IPrincipalAccessor _principalAccessor;

        public CurrentCompanyNameProvider(
            IAppSqlServerRepository repository,
            IPrincipalAccessor principalAccessor)
        {
            _repository = repository;
            _principalAccessor = principalAccessor;
        }

        public async Task<string> GetAsync()
        {
            var claims = _principalAccessor.Principal?.Claims;
            var companyPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.CompanyPk)?.Value;
            var branchPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.BranchPk)?.Value;

            if (!string.IsNullOrWhiteSpace(companyPk))
            {
                return await _repository.QueryFirstOrDefaultAsync<string>(
                    "SELECT TOP 1 GC_Name FROM GlbCompany WHERE GC_PK = @companyPk",
                    new { companyPk }) ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(branchPk))
            {
                return await _repository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 c.GC_Name
FROM GlbBranch b
INNER JOIN GlbCompany c ON c.GC_PK = b.GB_GC
WHERE b.GB_PK = @branchPk", new { branchPk }) ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
