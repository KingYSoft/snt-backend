using Abp.Authorization;
using Abp.Application.Services.Dto;
using Abp.Localization;
using Dapper;
using Facade;
using SntBackend.Application.SystemSettings;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.App;
using SntBackend.DomainService.Share.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SntBackend.Application.Group
{
    public class GroupApplication : SntBackendApplicationBase, IGroupApplication
    {
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        private readonly IPermissionManager _permissionManager;
        private readonly ILocalizationContext _localizationContext;
        private readonly IPermissionCache _permissionCache;

        public GroupApplication(
            IAppSqlServerRepository appSqlServerRepository,
            IPermissionManager permissionManager,
            ILocalizationContext localizationContext,
            IPermissionCache permissionCache)
        {
            _appSqlServerRepository = appSqlServerRepository;
            _permissionManager = permissionManager;
            _localizationContext = localizationContext;
            _permissionCache = permissionCache;
        }

        public Task<GroupAllPermissionOutput> AllPermission()
        {
            var roots = _permissionManager.GetAllPermissions().Where(x => x.Parent == null);
            return Task.FromResult(new GroupAllPermissionOutput
            {
                list = BuildPermissionTree(roots)
            });
        }

        public async Task<PagedResultDto<GroupDtoOutput>> QueryPage(GroupQueryInput input)
        {
            input ??= new GroupQueryInput();
            var parameters = new DynamicParameters();
            var conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(input.query))
            {
                parameters.Add("search", $"%{input.query.Trim()}%");
                conditions.Add("(code LIKE @search OR CAST([desc] AS nvarchar(max)) LIKE @search)");
            }

            var filterMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pk"] = "pk",
                ["code"] = "code",
                ["description"] = "CAST([desc] AS nvarchar(max))",
                ["desc"] = "CAST([desc] AS nvarchar(max))",
                ["is_admin"] = "is_admin",
                ["is_active"] = "is_active"
            };
            var filterIndex = 0;
            foreach (var filter in input.filters ?? new List<SystemFilterItem>())
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.val))
                    continue;

                if (!filterMap.TryGetValue(filter?.key ?? string.Empty, out var column))
                    throw new AppException($"Unsupported group filter: {filter?.key}");

                if (column == "is_active")
                {
                    if (!int.TryParse(filter.val.Trim(), out var isActive) || (isActive != 0 && isActive != 1))
                        throw new AppException("is_active filter must be 0 or 1.");

                    var op = string.IsNullOrWhiteSpace(filter.op) ? "Equal" : filter.op.Trim();
                    if (op != "Equal" && op != "Not Equal" && op != "NotEqual")
                        throw new AppException($"Unsupported is_active filter operator: {op}");

                    var parameterName = $"filter_{filterIndex++}";
                    parameters.Add(parameterName, isActive);
                    conditions.Add($"{column} {(op == "Equal" ? "=" : "<>")} @{parameterName}");
                    continue;
                }

                var condition = SystemSettingsQueryHelper.BuildTextFilter(
                    column, filter, parameters, $"filter_{filterIndex++}");
                if (!string.IsNullOrWhiteSpace(condition))
                    conditions.Add(condition);
            }

            var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
            var skip = SystemSettingsQueryHelper.NormalizeSkip(input.SkipCount);
            var limit = SystemSettingsQueryHelper.NormalizeLimit(input.MaxResultCount);
            parameters.Add("skip", skip);
            parameters.Add("limit", limit);

            var total = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(
                $"SELECT COUNT(1) FROM SYS_GROUP {where}", parameters);
            var items = await _appSqlServerRepository.QueryAsync<GroupDtoOutput>(@$"
SELECT pk, code, [desc] AS description, [desc] AS [desc], is_admin, is_active
FROM SYS_GROUP
{where}
ORDER BY code, pk
OFFSET @skip ROWS FETCH NEXT @limit ROWS ONLY", parameters);

            return new PagedResultDto<GroupDtoOutput>
            {
                TotalCount = total,
                Items = items.ToList()
            };
        }

        public async Task<GroupDetailOutput> Detail(string pk)
        {
            if (string.IsNullOrWhiteSpace(pk))
                throw new AppException("Group primary key is required.");

            var group = await _appSqlServerRepository.QueryFirstOrDefaultAsync<GroupDtoOutput>(@"
SELECT TOP 1 pk, code, [desc] AS description, [desc] AS [desc], is_admin, is_active
FROM SYS_GROUP WHERE pk = @pk", new { pk });
            if (group == null)
                throw new AppException("Group not found.");

            var users = await _appSqlServerRepository.QueryAsync<UserDtoOutput>(@"
SELECT DISTINCT
    s.gs_pk AS pk, s.gs_code AS code, s.gs_loginname AS login_name,
    s.gs_fullname AS full_name, s.gs_emailaddress AS email_address,
    s.gs_workphone AS work_phone, s.gs_mobilephone AS mobile_phone,
    s.gs_gb_homebranch AS home_branch, s.gs_ge_homedepartment AS home_department,
    s.gs_rn_nkcountrycode AS country_code, s.gs_isactive AS is_active,
    s.gs_isvalid AS is_valid, s.gs_canlogin AS can_login
FROM GlbStaff s
INNER JOIN SYS_GROUP_USER gu ON gu.user_pk = s.gs_pk
WHERE gu.group_pk = @pk
ORDER BY s.gs_loginname, s.gs_pk", new { pk });

            var hasDepartmentTable = await HasDepartmentTable();
            var departmentSelect = hasDepartmentTable
                ? "d.code AS dept_code, d.name AS dept_name"
                : "CAST(NULL AS nvarchar(200)) AS dept_code, CAST(NULL AS nvarchar(200)) AS dept_name";
            var departmentJoin = hasDepartmentTable
                ? "LEFT JOIN SYS_DEPT d ON d.pk = p.dept_pk"
                : string.Empty;
            var permissionRows = await _appSqlServerRepository.QueryAsync<GroupPermissionDbRow>($@"
SELECT p.company_pk, p.branch_pk, p.dept_pk, p.is_allow,
       c.gc_code AS company_code, c.gc_name AS company_name,
       b.gb_code AS branch_code, b.gb_branchname AS branch_name,
       {departmentSelect},
       n.permission_name
FROM SYS_GROUP_PERMISSION p
LEFT JOIN SYS_GROUP_PERMISSION_NAME n ON n.group_permission_pk = p.pk
LEFT JOIN GlbCompany c ON c.gc_pk = p.company_pk
LEFT JOIN GlbBranch b ON b.gb_pk = p.branch_pk
{departmentJoin}
WHERE p.group_pk = @pk
ORDER BY p.company_pk, p.branch_pk, p.dept_pk, p.is_allow, n.permission_name", new { pk });

            var grouped = permissionRows
                .GroupBy(x => new
                {
                    x.company_code,
                    x.branch_code,
                    x.dept_code,
                    x.is_allow
                })
                .Select(x => new GroupPermissionRowOutput
                {
                    company_pks = x.Where(y => !string.IsNullOrWhiteSpace(y.company_pk))
                        .Select(y => y.company_pk).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    company_code = x.Select(y => y.company_code).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    company_name = x.Select(y => y.company_name).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    branch_pks = x.Where(y => !string.IsNullOrWhiteSpace(y.branch_pk))
                        .Select(y => y.branch_pk).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    branch_code = x.Select(y => y.branch_code).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    branch_name = x.Select(y => y.branch_name).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    dept_pks = x.Where(y => !string.IsNullOrWhiteSpace(y.dept_pk))
                        .Select(y => y.dept_pk).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    dept_code = x.Select(y => y.dept_code).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    dept_name = x.Select(y => y.dept_name).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    is_allow = x.Key.is_allow,
                    permission_names = x.Where(y => !string.IsNullOrWhiteSpace(y.permission_name))
                        .Select(y => y.permission_name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                }).ToList();

            return new GroupDetailOutput
            {
                group_detail = group,
                users = users.ToList(),
                permission_rows = grouped
            };
        }

        public async Task<GroupSaveOutput> Save(GroupSaveInput input)
        {
            if (input == null)
                throw new AppException("Group code is required.");

            var groupDetail = input.group_detail;
            var inputPk = groupDetail?.pk ?? input.pk;
            var inputCode = groupDetail?.code ?? input.code;
            var inputDescription = groupDetail?.description ?? groupDetail?.desc ?? input.description ?? input.desc;
            var inputIsAdmin = groupDetail?.is_admin ?? input.is_admin;
            var inputIsActive = groupDetail?.is_active ?? input.is_active;
            if (string.IsNullOrWhiteSpace(inputCode))
                throw new AppException("Group code is required.");

            var code = inputCode.Trim();
            var isAdmin = string.IsNullOrWhiteSpace(inputIsAdmin) ? "N" : inputIsAdmin.Trim().ToUpperInvariant();
            if (isAdmin != "Y" && isAdmin != "N")
                throw new AppException("is_admin must be Y or N.");
            var pk = string.IsNullOrWhiteSpace(inputPk) ? SystemSettingsQueryHelper.NewPk() : inputPk.Trim();
            var duplicate = await _appSqlServerRepository.QueryFirstOrDefaultAsync<string>(@"
SELECT TOP 1 pk FROM SYS_GROUP
WHERE code = @code AND (@pk IS NULL OR pk <> @pk)",
                new { code, pk = string.IsNullOrWhiteSpace(inputPk) ? null : inputPk.Trim() });
            if (!string.IsNullOrWhiteSpace(duplicate))
                throw new AppException("Group code already exists.");

            var users = (input.selected_user_pks ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
            if (users.Count > 0)
            {
                var existingUserCount = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(@"
SELECT COUNT(DISTINCT s.gs_pk) FROM GlbStaff s
WHERE s.gs_pk IN @user_pks
  AND (s.gs_isactive = 1 AND s.gs_isvalid = 1
       OR EXISTS (
           SELECT 1 FROM SYS_GROUP_USER gu
           WHERE gu.user_pk = s.gs_pk AND gu.group_pk = @group_pk
       ))", new { user_pks = users, group_pk = pk });
                if (existingUserCount != users.Count)
                    throw new AppException("One or more group users do not exist or are inactive.");
            }

            var permissionRows = input.permission_rows ?? new List<GroupPermissionRowInput>();
            var organizationScopes = await LoadOrganizationScopes();
            var permissionNames = permissionRows.Where(x => x != null)
                .SelectMany(x => x.permission_names ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var permissionName in permissionNames)
            {
                if (_permissionManager.GetPermissionOrNull(permissionName) == null)
                    throw new AppException($"Permission does not exist: {permissionName}");
            }

            var parameters = new DynamicParameters();
            parameters.Add("group_pk", pk);
            parameters.Add("code", code);
            parameters.Add("description", inputDescription);
            parameters.Add("is_admin", isAdmin);
            parameters.Add("is_active", inputIsActive == 0 ? 0 : 1);

            var sql = @"
IF EXISTS (SELECT 1 FROM SYS_GROUP WHERE pk = @group_pk)
BEGIN
    UPDATE SYS_GROUP
    SET code = @code, [desc] = @description, is_admin = @is_admin, is_active = @is_active
    WHERE pk = @group_pk;
END
ELSE
BEGIN
    INSERT INTO SYS_GROUP (pk, code, [desc], is_admin, is_active)
    VALUES (@group_pk, @code, @description, @is_admin, @is_active);
END;
DELETE FROM SYS_GROUP_USER WHERE group_pk = @group_pk;
DELETE n FROM SYS_GROUP_PERMISSION_NAME n
INNER JOIN SYS_GROUP_PERMISSION p ON p.pk = n.group_permission_pk
WHERE p.group_pk = @group_pk;
DELETE FROM SYS_GROUP_PERMISSION WHERE group_pk = @group_pk;
";

            var userIndex = 0;
            foreach (var userPk in users)
            {
                var name = $"user_pk_{userIndex++}";
                parameters.Add(name, userPk);
                sql += $"INSERT INTO SYS_GROUP_USER (pk, user_pk, group_pk) VALUES ('{Guid.NewGuid()}', @{name}, @group_pk);\n";
            }

            var permissionIndex = 0;
            foreach (var row in permissionRows)
            {
                if (row == null)
                    throw new AppException("Permission rows cannot contain null values.");

                var companyPks = NormalizeValues(row.company_pks);
                var branchPks = NormalizeValues(row.branch_pks);
                var deptPks = NormalizeValues(row.dept_pks);
                var scopes = ExpandScopes(companyPks, branchPks, deptPks, organizationScopes);
                if (scopes.Count == 0)
                {
                    if ((row.permission_names?.Count ?? 0) == 0)
                        continue;

                    // A null organization scope is a global system permission.
                    // This keeps the explicit bootstrap administrator editable.
                    scopes.Add((null, null, null));
                }

                foreach (var scope in scopes)
                {
                    var permissionPk = Guid.NewGuid().ToString();
                    var permissionName = $"permission_pk_{permissionIndex}";
                    parameters.Add(permissionName, permissionPk);
                    parameters.Add($"company_pk_{permissionIndex}", scope.company_pk);
                    parameters.Add($"branch_pk_{permissionIndex}", scope.branch_pk);
                    parameters.Add($"dept_pk_{permissionIndex}", scope.dept_pk);
                    var isAllow = string.IsNullOrWhiteSpace(row.is_allow) ? "Y" : row.is_allow.Trim().ToUpperInvariant();
                    if (isAllow != "Y" && isAllow != "N")
                        throw new AppException("is_allow must be Y or N.");
                    parameters.Add($"is_allow_{permissionIndex}", isAllow);
                    sql += $@"INSERT INTO SYS_GROUP_PERMISSION
    (pk, group_pk, company_pk, branch_pk, dept_pk, is_allow)
VALUES (@{permissionName}, @group_pk, @company_pk_{permissionIndex}, @branch_pk_{permissionIndex}, @dept_pk_{permissionIndex}, @is_allow_{permissionIndex});
";
                    var permissionNamesToSave = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var permission in row.permission_names ?? new List<string>())
                    {
                        if (string.IsNullOrWhiteSpace(permission))
                            continue;

                        var permissionDefinition = _permissionManager.GetPermissionOrNull(permission.Trim());
                        if (permissionDefinition == null)
                            continue;

                        permissionNamesToSave.Add(permissionDefinition.Name);

                        var parent = permissionDefinition.Parent;
                        while (parent != null)
                        {
                            if (!string.Equals(parent.Name, PermissionNameConsts.System, StringComparison.OrdinalIgnoreCase))
                                permissionNamesToSave.Add(parent.Name);
                            parent = parent.Parent;
                        }
                    }
                    foreach (var permissionNameToSave in permissionNamesToSave)
                    {
                        var permissionValueName = $"permission_name_{permissionIndex}_{Guid.NewGuid():N}";
                        parameters.Add(permissionValueName, permissionNameToSave);
                        sql += $"INSERT INTO SYS_GROUP_PERMISSION_NAME (pk, group_permission_pk, permission_name) VALUES ('{Guid.NewGuid()}', @{permissionName}, @{permissionValueName});\n";
                    }
                    permissionIndex++;
                }
            }

            await _appSqlServerRepository.ExecuteAsync("BEGIN TRY BEGIN TRANSACTION;" + sql + "COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; THROW; END CATCH;", parameters);
            await _permissionCache.ClearAllAsync();
            var saved = (await QueryPage(new GroupQueryInput { query = code, MaxResultCount = 1 })).Items.FirstOrDefault(x => x.pk == pk);
            return new GroupSaveOutput { pk = pk, group = saved };
        }

        public async Task<CompanyBranchDeptOptionsOutput> QueryCompanyBranchDeptOptions()
        {
            var hasDepartmentTable = await HasDepartmentTable();
            var companies = (await _appSqlServerRepository.QueryAsync<CompanyOptionOutput>(@"
SELECT gc_pk AS pk, gc_code AS code, gc_name AS name
FROM GlbCompany WHERE gc_isactive = 1 AND gc_isvalid = 1
ORDER BY gc_code")).ToList();
            var branches = (await _appSqlServerRepository.QueryAsync<BranchOptionOutput>(@"
SELECT gb_pk AS pk, gb_code AS code, gb_branchname AS name, gb_gc AS company_pk
FROM GlbBranch WHERE gb_isactive = 1 AND gb_isvalid = 1
ORDER BY gb_code")).ToList();
            var departments = hasDepartmentTable
                ? (await _appSqlServerRepository.QueryAsync<DeptOptionDbRow>(@"
SELECT pk, code, name, sys_branch AS branch_pk
FROM SYS_DEPT
WHERE is_active = 1
ORDER BY code, pk")).ToList()
                : new List<DeptOptionDbRow>();

            var result = companies.Select(company =>
            {
                var output = new CompanyOptionOutput
                {
                    pk = company.pk,
                    code = company.code,
                    name = company.name,
                    company_code = company.code,
                    company_name = company.name,
                    company_pks = new List<string> { company.pk },
                    branch_list = branches.Where(x => x.company_pk == company.pk)
                        .Select(x => new BranchOptionOutput
                        {
                            pk = x.pk,
                            code = x.code,
                            name = x.name,
                            branch_code = x.code,
                            branch_name = x.name,
                            branch_pks = new List<string> { x.pk },
                            dept_list = departments.Where(d => d.branch_pk == x.pk)
                                .Select(d => new DeptOptionOutput
                                {
                                    pk = d.pk,
                                    code = d.code,
                                    name = d.name,
                                    dept_code = d.code,
                                    dept_name = d.name,
                                    dept_pks = new List<string> { d.pk }
                                })
                                .ToList()
                        })
                        .ToList()
                };
                return output;
            }).ToList();

            return new CompanyBranchDeptOptionsOutput { company_list = result };
        }

        private List<PermissionDtoOutput> BuildPermissionTree(IEnumerable<Permission> permissions)
        {
            return permissions.Select(permission => new PermissionDtoOutput
            {
                name = permission.Name,
                permission_name = permission.Name,
                display_name = permission.DisplayName?.Localize(_localizationContext) ?? permission.Name,
                children = BuildPermissionTree(permission.Children)
            }).ToList();
        }

        private static List<string> NormalizeValues(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct().ToList();
        }

        private static List<(string company_pk, string branch_pk, string dept_pk)> ExpandScopes(
            List<string> companyPks,
            List<string> branchPks,
            List<string> deptPks,
            IReadOnlyCollection<OrganizationScopeDbRow> organizationScopes)
        {
            var result = new List<(string company_pk, string branch_pk, string dept_pk)>();
            if (deptPks.Count > 0)
            {
                foreach (var deptPk in deptPks)
                {
                    var scope = organizationScopes.FirstOrDefault(x => x.dept_pk == deptPk);
                    if (scope == null)
                        throw new AppException("One or more department scopes are invalid or inactive.");

                    result.Add((scope.company_pk, scope.branch_pk, scope.dept_pk));
                }
            }
            else if (branchPks.Count > 0)
            {
                foreach (var branchPk in branchPks)
                {
                    var scope = organizationScopes.FirstOrDefault(x => x.branch_pk == branchPk);
                    if (scope == null)
                        throw new AppException("One or more branch scopes are invalid or inactive.");

                    result.Add((scope.company_pk, branchPk, null));
                }
            }
            else
            {
                foreach (var companyPk in companyPks)
                {
                    if (!organizationScopes.Any(x => x.company_pk == companyPk))
                        throw new AppException("One or more company scopes are invalid or inactive.");

                    result.Add((companyPk, null, null));
                }
            }
            return result.Distinct().ToList();
        }

        private async Task<List<OrganizationScopeDbRow>> LoadOrganizationScopes()
        {
            if (!await HasDepartmentTable())
            {
                return (await _appSqlServerRepository.QueryAsync<OrganizationScopeDbRow>(@"
SELECT
    c.gc_pk AS company_pk,
    b.gb_pk AS branch_pk,
    CAST(NULL AS nvarchar(36)) AS dept_pk
FROM GlbCompany c
LEFT JOIN GlbBranch b
    ON b.gb_gc = c.gc_pk
   AND b.gb_isactive = 1
   AND b.gb_isvalid = 1
WHERE c.gc_isactive = 1
  AND c.gc_isvalid = 1")).ToList();
            }

            return (await _appSqlServerRepository.QueryAsync<OrganizationScopeDbRow>(@"
SELECT
    c.gc_pk AS company_pk,
    b.gb_pk AS branch_pk,
    d.pk AS dept_pk
FROM GlbCompany c
LEFT JOIN GlbBranch b
    ON b.gb_gc = c.gc_pk
   AND b.gb_isactive = 1
   AND b.gb_isvalid = 1
LEFT JOIN SYS_DEPT d
    ON d.sys_company = c.gc_pk
   AND d.sys_branch = b.gb_pk
   AND d.is_active = 1
WHERE c.gc_isactive = 1
  AND c.gc_isvalid = 1")).ToList();
        }

        private async Task<bool> HasDepartmentTable()
        {
            var tableExists = await _appSqlServerRepository.QueryFirstOrDefaultAsync<int>(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.sys_dept', N'U') IS NULL THEN 0 ELSE 1 END");
            return tableExists == 1;
        }

        private sealed class GroupPermissionDbRow
        {
            public string company_pk { get; set; }
            public string company_code { get; set; }
            public string company_name { get; set; }
            public string branch_pk { get; set; }
            public string branch_code { get; set; }
            public string branch_name { get; set; }
            public string dept_pk { get; set; }
            public string dept_code { get; set; }
            public string dept_name { get; set; }
            public string is_allow { get; set; }
            public string permission_name { get; set; }
        }

        private sealed class DeptOptionDbRow
        {
            public string pk { get; set; }
            public string code { get; set; }
            public string name { get; set; }
            public string branch_pk { get; set; }
        }

        private sealed class OrganizationScopeDbRow
        {
            public string company_pk { get; set; }
            public string branch_pk { get; set; }
            public string dept_pk { get; set; }
        }
    }
}
