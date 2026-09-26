using Abp.Dependency;
using Abp.Runtime.Session;
using Dapper;
using Facade;
using SntBackend.DomainService.Share;
using SntBackend.DomainService.Share.App;
using SntBackend.DomainService.Share.Authorization;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Authorization
{
    public sealed class SystemAuditContextProvider : ITransientDependency
    {
        private readonly IAppSqlServerRepository _repository;
        private readonly IPrincipalAccessor _principalAccessor;

        public SystemAuditContextProvider(
            IAppSqlServerRepository repository,
            IPrincipalAccessor principalAccessor)
        {
            _repository = repository;
            _principalAccessor = principalAccessor;
        }

        public async Task<SystemAuditContext> GetAsync()
        {
            var claims = _principalAccessor.Principal?.Claims;
            var staffPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            var branchPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.BranchPk)?.Value;
            var deptPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.DeptPk)?.Value;

            if (string.IsNullOrWhiteSpace(staffPk))
                throw new AppException("Current operator is required.");

            var context = await _repository.QueryFirstOrDefaultAsync<SystemAuditContext>(@"
SELECT TOP 1
    s.GS_Code AS UserCode,
    ISNULL(b.GB_Code, '') AS BranchCode,
    ISNULL(d.GE_Code, '') AS DepartmentCode
FROM GlbStaff s
LEFT JOIN GlbBranch b ON b.GB_PK = @branchPk
LEFT JOIN GlbDepartment d ON d.GE_PK = @deptPk
WHERE s.GS_PK = @staffPk", new { staffPk, branchPk, deptPk });

            if (context == null || string.IsNullOrWhiteSpace(context.UserCode))
                throw new AppException("Current operator was not found.");

            return context;
        }
    }

    public sealed class SystemAuditContext
    {
        public string UserCode { get; set; }
        public string BranchCode { get; set; }
        public string DepartmentCode { get; set; }
    }
}
