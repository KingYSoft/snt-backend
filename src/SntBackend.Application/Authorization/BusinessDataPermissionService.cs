using Abp.Dependency;
using Abp.Runtime.Session;
using Dapper;
using Facade;
using SntBackend.DomainService.Share.App;
using SntBackend.DomainService.Share.Authorization;
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Authorization
{
    /// <summary>
    /// 统一生成业务数据权限条件。
    /// 业务表沿用现有 company/branch/department pk 字段，权限范围来自 SYS_GROUP_PERMISSION。
    /// 管理员组可跳过业务组织范围和当前 Token 组织上下文过滤。
    /// </summary>
    public sealed class BusinessDataPermissionService : ITransientDependency
    {
        private readonly IAppSqlServerRepository _repository;
        private readonly IPrincipalAccessor _principalAccessor;

        public BusinessDataPermissionService(
            IAppSqlServerRepository repository,
            IPrincipalAccessor principalAccessor)
        {
            _repository = repository;
            _principalAccessor = principalAccessor;
        }

        /// <summary>
        /// 返回可直接拼接到 WHERE/AND 后的 EXISTS 条件。
        /// scope 表中全部组织字段为空表示全局权限；否则按公司、分公司、部门逐级匹配。
        /// </summary>
        public string BuildScopePredicate(
            string permissionName,
            string companyColumn,
            string branchColumn,
            string departmentColumn,
            DynamicParameters parameters,
            string parameterPrefix)
        {
            if (string.IsNullOrWhiteSpace(permissionName))
                throw new ArgumentException("Permission name is required.", nameof(permissionName));

            var claims = _principalAccessor.Principal?.Claims;
            var staffPk = claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            if (string.IsNullOrWhiteSpace(staffPk))
                return "1 = 0";

            var prefix = string.IsNullOrWhiteSpace(parameterPrefix) ? "permission_scope" : parameterPrefix;
            parameters.Add($"{prefix}_staff_pk", staffPk);
            parameters.Add($"{prefix}_permission_name", permissionName);
            parameters.Add($"{prefix}_company_pk", claims?.FirstOrDefault(x => x.Type == SntClaimTypes.CompanyPk)?.Value);
            parameters.Add($"{prefix}_branch_pk", claims?.FirstOrDefault(x => x.Type == SntClaimTypes.BranchPk)?.Value);
            parameters.Add($"{prefix}_dept_pk", claims?.FirstOrDefault(x => x.Type == SntClaimTypes.DeptPk)?.Value);

            return $@"EXISTS
(
    SELECT 1
    FROM SYS_GROUP_USER gu
    INNER JOIN SYS_GROUP g
        ON g.pk = gu.group_pk
       AND g.is_active = 1
    LEFT JOIN SYS_GROUP_PERMISSION gp
        ON gp.group_pk = g.pk
       AND gp.is_allow = 'Y'
    LEFT JOIN SYS_GROUP_PERMISSION_NAME gpn
        ON gpn.group_permission_pk = gp.pk
    WHERE gu.user_pk = @{prefix}_staff_pk
      AND
      (
          g.is_admin = 'Y'
          OR
          (
              gpn.permission_name = @{prefix}_permission_name
              AND (@{prefix}_company_pk IS NULL OR {companyColumn} = @{prefix}_company_pk)
              AND (@{prefix}_branch_pk IS NULL OR {branchColumn} = @{prefix}_branch_pk)
              AND (@{prefix}_dept_pk IS NULL OR {departmentColumn} = @{prefix}_dept_pk)
              AND
              (
                  (gp.company_pk IS NULL AND gp.branch_pk IS NULL AND gp.dept_pk IS NULL)
                  OR
                  ({companyColumn} = gp.company_pk AND gp.branch_pk IS NULL AND gp.dept_pk IS NULL)
                  OR
                  ({branchColumn} = gp.branch_pk AND gp.dept_pk IS NULL)
                  OR
                  ({departmentColumn} = gp.dept_pk)
              )
          )
      )
)";
        }

        /// <summary>
        /// 为交易头生成业务权限条件。交易头优先通过 ah_jh 关联作业头；历史数据 ah_jh 为空时，回退到交易行 al_jh。
        /// </summary>
        public string BuildTransactionScopePredicate(
            string permissionName,
            string headerAlias,
            DynamicParameters parameters,
            string parameterPrefix)
        {
            var scopePredicate = BuildScopePredicate(
                permissionName,
                "jh_scope.jh_gc",
                "jh_scope.jh_gb",
                "jh_scope.jh_ge",
                parameters,
                parameterPrefix);

            return $@"EXISTS
(
    SELECT 1
    FROM JobHeader jh_scope
    WHERE
    (
        jh_scope.jh_pk = {headerAlias}.ah_jh
        OR
        (
            {headerAlias}.ah_jh IS NULL
            AND EXISTS
            (
                SELECT 1
                FROM AccTransactionLines al_scope
                WHERE al_scope.al_ah = {headerAlias}.ah_pk
                  AND al_scope.al_jh = jh_scope.jh_pk
            )
        )
    )
    AND {scopePredicate}
)";
        }

        /// <summary>为多个业务权限生成 OR 条件，适用于 AR/AP 共用的结算查询。</summary>
        public string BuildAnyTransactionScopePredicate(
            IEnumerable<string> permissionNames,
            string headerAlias,
            DynamicParameters parameters,
            string parameterPrefix)
        {
            var names = permissionNames
                ?.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (names.Count == 0)
                return "1 = 0";

            var predicates = names
                .Select((name, index) => BuildTransactionScopePredicate(
                    name,
                    headerAlias,
                    parameters,
                    $"{parameterPrefix}_{index}"))
                .ToList();
            return $"({string.Join(" OR ", predicates)})";
        }

        /// <summary>为直接挂在作业头上的业务主表生成权限条件。</summary>
        public string BuildParentScopePredicate(
            string permissionName,
            string parentAlias,
            string parentPkColumn,
            string parentTableCode,
            DynamicParameters parameters,
            string parameterPrefix)
        {
            var scopePredicate = BuildScopePredicate(
                permissionName,
                "jh_scope.jh_gc",
                "jh_scope.jh_gb",
                "jh_scope.jh_ge",
                parameters,
                parameterPrefix);

            return $@"EXISTS
(
    SELECT 1
    FROM JobHeader jh_scope
    WHERE jh_scope.jh_parentid = {parentAlias}.{parentPkColumn}
      AND jh_scope.jh_parenttablecode = '{parentTableCode}'
      AND {scopePredicate}
)";
        }

        /// <summary>为合单主表生成权限条件；合单权限沿用其关联运单的作业头组织字段。</summary>
        public string BuildConsolScopePredicate(
            string permissionName,
            string consolAlias,
            string consolPkColumn,
            DynamicParameters parameters,
            string parameterPrefix)
        {
            var scopePredicate = BuildScopePredicate(
                permissionName,
                "jh_scope.jh_gc",
                "jh_scope.jh_gb",
                "jh_scope.jh_ge",
                parameters,
                parameterPrefix);

            return $@"EXISTS
(
    SELECT 1
    FROM JobConShipLink link_scope
    INNER JOIN JobHeader jh_scope
        ON jh_scope.jh_parentid = link_scope.jn_js
       AND jh_scope.jh_parenttablecode = 'JS'
    WHERE link_scope.jn_jk = {consolAlias}.{consolPkColumn}
      AND {scopePredicate}
)";
        }

        /// <summary>为合单成本主表生成权限条件；成本行通过 E6_ParentID 关联合单，再沿合单关联运单取组织范围。</summary>
        public string BuildConsolCostScopePredicate(
            string permissionName,
            string costAlias,
            string costParentColumn,
            DynamicParameters parameters,
            string parameterPrefix)
        {
            var scopePredicate = BuildScopePredicate(
                permissionName,
                "jh_scope.jh_gc",
                "jh_scope.jh_gb",
                "jh_scope.jh_ge",
                parameters,
                parameterPrefix);

            return $@"EXISTS
(
    SELECT 1
    FROM JobConShipLink link_scope
    INNER JOIN JobHeader jh_scope
        ON jh_scope.jh_parentid = link_scope.jn_js
       AND jh_scope.jh_parenttablecode = 'JS'
    WHERE link_scope.jn_jk = {costAlias}.{costParentColumn}
      AND {scopePredicate}
)";
        }

        /// <summary>校验当前用户是否能访问锚点下的作业数据。</summary>
        public async Task EnsureAnchorAccessAsync(
            string parentTable,
            string parentPkColumn,
            string parentTableCode,
            string cancelledColumn,
            string anchorPk,
            string permissionName)
        {
            if (string.IsNullOrWhiteSpace(anchorPk))
                throw new AppException("Business record is required.");

            var parameters = new DynamicParameters();
            if (Guid.TryParse(anchorPk, out var anchorGuid))
                parameters.Add("anchorPk", anchorGuid, DbType.Guid);
            else
                parameters.Add("anchorPk", anchorPk);
            parameters.Add("parentTableCode", parentTableCode);

            var scopePredicate = BuildScopePredicate(
                permissionName,
                "jh.jh_gc",
                "jh.jh_gb",
                "jh.jh_ge",
                parameters,
                "anchor_scope");

            var exists = await _repository.QueryFirstOrDefaultAsync<int>($@"
SELECT TOP 1 1
FROM JobHeader jh
INNER JOIN {parentTable} anchor ON anchor.{parentPkColumn} = jh.jh_parentid
WHERE jh.jh_parentid = @anchorPk
  AND jh.jh_parenttablecode = @parentTableCode
  AND anchor.{cancelledColumn} = 0
  AND {scopePredicate}", parameters);

            if (exists != 1)
                throw new AppException("You do not have permission to access this business data.");
        }

        /// <summary>解析并校验用户可切换到的部门组织范围。</summary>
        public async Task<OrganizationScope> GetAccessibleDepartmentAsync(string staffPk, string deptPk)
        {
            if (string.IsNullOrWhiteSpace(staffPk) || string.IsNullOrWhiteSpace(deptPk))
                return null;

            return await _repository.QueryFirstOrDefaultAsync<OrganizationScope>(@"
SELECT TOP 1
    d.pk AS dept_pk,
    d.sys_branch AS branch_pk,
    d.sys_company AS company_pk
FROM SYS_DEPT d
WHERE d.pk = @deptPk
  AND d.is_active = 1
  AND EXISTS
  (
      SELECT 1
      FROM SYS_GROUP_USER gu
      INNER JOIN SYS_GROUP g
          ON g.pk = gu.group_pk
         AND g.is_active = 1
      LEFT JOIN SYS_GROUP_PERMISSION gp
          ON gp.group_pk = g.pk
         AND gp.is_allow = N'Y'
      LEFT JOIN SYS_GROUP_PERMISSION_NAME gpn
          ON gpn.group_permission_pk = gp.pk
      WHERE gu.user_pk = @staffPk
        AND
        (
            g.is_admin = N'Y'
            OR gpn.permission_name IN @organizationPermissions
        )
        AND
        (
            (gp.company_pk IS NULL AND gp.branch_pk IS NULL AND gp.dept_pk IS NULL)
            OR (gp.company_pk = d.sys_company AND gp.branch_pk IS NULL AND gp.dept_pk IS NULL)
            OR (gp.branch_pk = d.sys_branch AND gp.dept_pk IS NULL)
            OR (gp.dept_pk = d.pk)
        )
  )", new
            {
                staffPk,
                deptPk,
                organizationPermissions = new[]
                {
                    PermissionNameConsts.Business,
                    PermissionNameConsts.Settlement
                }
            });
        }

        public sealed class OrganizationScope
        {
            public string company_pk { get; set; }
            public string branch_pk { get; set; }
            public string dept_pk { get; set; }
        }
    }
}
