using Abp.Runtime.Security;
using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using SntBackend.Application;
using SntBackend.Application.Authorization;
using SntBackend.Web.Core.Authentication.JwtBearer;
using SntBackend.Web.Core.Controllers;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Abp.Auditing;
using SntBackend.Application.User;
using SntBackend.Application.User.Dto;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.Authorization;
using SntBackend.DomainService.Share.App;
using SntBackend.Web.Core.Authorization;

namespace SntBackend.Web.Host.Controllers
{
    [Route("user")]
    public class UserController : SntBackendControllerBase
    {
        private const string LoginFailedMessage = "Incorrect username or password.";
        private readonly TokenAuthConfiguration _configuration;
        private readonly IAppSqlServerRepository _appSqlServerRepository;
        private readonly PasswordHashService _passwordHashService;
        private readonly IUserApplication _userApplication;
        private readonly SystemPermissionChecker _permissionChecker;
        private readonly BusinessDataPermissionService _businessDataPermissionService;
        private readonly IPermissionCache _permissionCache;

        public UserController(
            TokenAuthConfiguration configuration,
            IAppSqlServerRepository appSqlServerRepository,
            PasswordHashService passwordHashService,
            IUserApplication userApplication,
            SystemPermissionChecker permissionChecker,
            BusinessDataPermissionService businessDataPermissionService,
            IPermissionCache permissionCache)
        {
            _configuration = configuration;
            _appSqlServerRepository = appSqlServerRepository;
            _passwordHashService = passwordHashService;
            _userApplication = userApplication;
            _permissionChecker = permissionChecker;
            _businessDataPermissionService = businessDataPermissionService;
            _permissionCache = permissionCache;
        }

        [HttpGet]
        [Route("query-page")]
        public async Task<JsonResponse<Abp.Application.Services.Dto.PagedResultDto<UserDtoOutput>>> QueryPage(
            [FromQuery] UserQueryInput input)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemUser);
            return new JsonResponse<Abp.Application.Services.Dto.PagedResultDto<UserDtoOutput>>
            {
                Data = await _userApplication.QueryPage(input)
            };
        }

        [HttpPost]
        [Route("save")]
        public async Task<JsonResponse<UserDtoOutput>> Save([FromBody] UserSaveInput input)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemUser);
            return new JsonResponse<UserDtoOutput>
            {
                Data = await _userApplication.Save(input)
            };
        }

        [HttpPost]
        [Route("delete/{pk}")]
        public async Task<JsonResponse> Delete(string pk)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemUser);
            await _userApplication.Delete(pk);
            return new JsonResponse();
        }

        [HttpGet]
        [Route("detail")]
        public async Task<JsonResponse<UserDtoOutput>> Detail([FromQuery] string pk)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemUser);
            return new JsonResponse<UserDtoOutput>
            {
                Data = await _userApplication.Detail(pk)
            };
        }

        [HttpGet]
        [Route("query-all")]
        public async Task<JsonResponse<UserQueryAllOutput>> QueryAll([FromQuery] string query)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemUser);
            return new JsonResponse<UserQueryAllOutput>
            {
                Data = await _userApplication.QueryAll(query)
            };
        }
        #region login
        [HttpPost]
        [NoToken]
        [Route("login")]
        [DisableAuditing]
        public async Task<JsonResponse<UserLoginOutput>> Login([FromBody] UserLoginInput input)
        {
            var loginName = input?.email?.Trim();
            if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrEmpty(input?.password))
            {
                return new JsonResponse<UserLoginOutput>(false, LoginFailedMessage);
            }

            var staff = await _appSqlServerRepository.QueryFirstOrDefaultAsync<GlbStaffLoginRecord>(
                @"SELECT TOP 1
                         GS_PK AS gs_pk,
                         GS_LoginName AS gs_loginname,
                         GS_FullName AS gs_fullname,
                         GS_EmailAddress AS gs_emailaddress,
                         GS_PasswordHash AS gs_passwordhash,
                         GS_PasswordSalt AS gs_passwordsalt,
                         GS_PasswordHashIterations AS gs_passwordhashiterations,
                         s.GS_GB_HomeBranch AS gs_gb_homebranch,
                         s.GS_GE_HomeDepartment AS gs_ge_homedepartment,
                         b.GB_GC AS gs_gc_homecompany
                  FROM GlbStaff s
                  LEFT JOIN GlbBranch b ON b.GB_PK = s.GS_GB_HomeBranch
                  WHERE s.GS_LoginName = @loginName
                    AND ISNULL(GS_CanLogin, 0) = 1
                    AND ISNULL(GS_IsActive, 0) = 1",
                new { loginName });

            if (staff == null || !_passwordHashService.Verify(
                    input.password,
                    staff.gs_passwordhash,
                    staff.gs_passwordsalt,
                    staff.gs_passwordhashiterations))
            {
                return new JsonResponse<UserLoginOutput>(false, LoginFailedMessage);
            }

            await _permissionCache.ClearAsync(staff.gs_pk);
            var isSystemAdmin = (await _permissionCache.GetGrantedNamesAsync(staff.gs_pk)).Contains("*");

            var identity = CreateClaimsIdentity(
                "1",
                staff.gs_loginname,
                string.Empty,
                staff.gs_pk,
                isSystemAdmin ? null : staff.gs_gc_homecompany,
                isSystemAdmin ? null : staff.gs_gb_homebranch,
                null);
            var accessToken = GetEncrpyedAccessToken(CreateAccessToken(CreateJwtClaims(identity)));

            return new JsonResponse<UserLoginOutput>
            {
                Data = new UserLoginOutput
                {
                    accessToken = accessToken,
                    full_name = staff.gs_fullname,
                    email_address = staff.gs_emailaddress,
                    login_name = staff.gs_loginname,
                    company_pk = isSystemAdmin ? null : staff.gs_gc_homecompany,
                    branch_pk = isSystemAdmin ? null : staff.gs_gb_homebranch,
                    dept_pk = null
                }
            };
        }

        [HttpPost]
        [Route("switch-branch")]
        [Route("switchBranch")]
        public async Task<JsonResponse<UserLoginOutput>> SwitchBranch([FromBody] UserSwitchBranchInput input)
        {
            var staffPk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            var companyPk = input?.company_pk?.Trim();
            var branchPk = input?.branch_pk?.Trim();
            if (string.IsNullOrWhiteSpace(staffPk))
                return new JsonResponse<UserLoginOutput>(false, "Login is invalid, please login again.");

            await _permissionCache.ClearAsync(staffPk);
            var isSystemAdmin = (await _permissionCache.GetGrantedNamesAsync(staffPk)).Contains("*");

            BusinessDataPermissionService.OrganizationScope scope;
            if (!string.IsNullOrWhiteSpace(branchPk))
            {
                scope = await _businessDataPermissionService.GetAccessibleBranchAsync(
                    staffPk,
                    companyPk,
                    branchPk,
                    isSystemAdmin);
            }
            else
            {
                return new JsonResponse<UserLoginOutput>(false, "Select a branch.");
            }

            if (scope == null)
                return new JsonResponse<UserLoginOutput>(false, "The selected branch is not accessible.");

            var staff = await _appSqlServerRepository.QueryFirstOrDefaultAsync<GlbStaffLoginRecord>(
                @"SELECT TOP 1
                         GS_PK AS gs_pk,
                         GS_LoginName AS gs_loginname,
                         GS_FullName AS gs_fullname,
                         GS_EmailAddress AS gs_emailaddress,
                         @companyPk AS gs_gc_homecompany
                  FROM GlbStaff
                  WHERE GS_PK = @staffPk
                    AND ISNULL(GS_CanLogin, 0) = 1
                    AND ISNULL(GS_IsActive, 0) = 1",
                new { staffPk, companyPk = scope.company_pk });
            if (staff == null)
                return new JsonResponse<UserLoginOutput>(false, LoginFailedMessage);

            var identity = CreateClaimsIdentity(
                "1",
                staff.gs_loginname,
                string.Empty,
                staff.gs_pk,
                scope.company_pk,
                scope.branch_pk,
                null);
            var accessToken = GetEncrpyedAccessToken(CreateAccessToken(CreateJwtClaims(identity)));

            return new JsonResponse<UserLoginOutput>
            {
                Data = new UserLoginOutput
                {
                    accessToken = accessToken,
                    full_name = staff.gs_fullname,
                    email_address = staff.gs_emailaddress,
                    login_name = staff.gs_loginname,
                    // Keep the selected organization in the new token even for
                    // administrators. Admin permissions still bypass data-scope
                    // filtering, while /user/session can report the selection.
                    company_pk = scope.company_pk,
                    branch_pk = scope.branch_pk,
                    dept_pk = null
                }
            };
        }

        [HttpGet]
        [Route("query-switch-tbl")]
        [Route("querySwitchTbl")]
        public async Task<JsonResponse<UserQuerySwitchTblOutput>> QuerySwitchTbl()
        {
            var staffPk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            if (string.IsNullOrWhiteSpace(staffPk))
                return new JsonResponse<UserQuerySwitchTblOutput>(false, "Login is invalid, please login again.");

            await _permissionCache.ClearAsync(staffPk);
            var isSystemAdmin = (await _permissionCache.GetGrantedNamesAsync(staffPk)).Contains("*");
            var rows = await _appSqlServerRepository.QueryAsync<UserSwitchOptionRecord>(@"
SELECT
    c.GC_PK AS company_pk,
    c.GC_Code AS company_code,
    c.GC_Name AS company_name,
    b.GB_PK AS branch_pk,
    b.GB_Code AS branch_code,
    b.GB_BranchName AS branch_name
FROM GlbCompany c
INNER JOIN GlbBranch b ON b.GB_GC = c.GC_PK
WHERE c.GC_IsActive = 1
  AND c.GC_IsValid = 1
  AND b.GB_IsActive = 1
  AND b.GB_IsValid = 1
  AND
  (
      @isSystemAdmin = 1
      OR EXISTS
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
                OR (gp.company_pk = c.GC_PK AND gp.branch_pk IS NULL AND gp.dept_pk IS NULL)
                OR (gp.branch_pk = b.GB_PK AND gp.dept_pk IS NULL)
            )
      )
  )
ORDER BY c.GC_Code, b.GB_Code", new
            {
                staffPk,
                isSystemAdmin,
                organizationPermissions = new[]
                {
                    PermissionNameConsts.Business,
                    PermissionNameConsts.Settlement
                }
            });

            var output = new UserQuerySwitchTblOutput();
            foreach (var row in rows)
            {
                var company = output.company_list.FirstOrDefault(x => x.company_pk == row.company_pk);
                if (company == null)
                {
                    company = new UserSwitchCompanyOutput
                    {
                        company_pk = row.company_pk,
                        company_code = row.company_code,
                        company_name = row.company_name
                    };
                    output.company_list.Add(company);
                }

                company.branch_list.Add(new UserSwitchBranchOutput
                {
                    branch_pk = row.branch_pk,
                    branch_code = row.branch_code,
                    branch_name = row.branch_name
                });
            }

            return new JsonResponse<UserQuerySwitchTblOutput> { Data = output };
        }

        [HttpGet]
        [Route("session")]
        public async Task<JsonResponse<UserSessionOutput>> Session()
        {
            var staffPk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            if (string.IsNullOrWhiteSpace(staffPk))
            {
                return new JsonResponse<UserSessionOutput>(false, "Login is invalid, please login again.");
            }

            var companyPk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.CompanyPk)?.Value;
            var branchPk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.BranchPk)?.Value;
            var session = await _appSqlServerRepository.QueryFirstOrDefaultAsync<UserSessionRecord>(@"
SELECT TOP 1
    s.GS_PK AS staff_pk,
    COALESCE(@companyPk, b.GB_GC) AS company_pk,
    c.GC_Code AS company_code,
    c.GC_Name AS company_name,
    b.GB_PK AS branch_pk,
    b.GB_Code AS branch_code,
    b.GB_BranchName AS branch_name,
    CAST(NULL AS uniqueidentifier) AS dept_pk,
    CAST(NULL AS varchar(3)) AS dept_code,
    CAST(NULL AS varchar(200)) AS dept_name
FROM GlbStaff s
LEFT JOIN GlbBranch b
    ON b.GB_PK = COALESCE(@branchPk, s.GS_GB_LastLogonBranch, s.GS_GB_HomeBranch)
LEFT JOIN GlbCompany c
    ON c.GC_PK = COALESCE(@companyPk, b.GB_GC)
WHERE s.GS_PK = @staffPk", new
            {
                staffPk,
                companyPk = string.IsNullOrWhiteSpace(companyPk) ? null : companyPk,
                branchPk = string.IsNullOrWhiteSpace(branchPk) ? null : branchPk
            });

            return new JsonResponse<UserSessionOutput>
            {
                Data = session == null
                    ? new UserSessionOutput { staff_pk = staffPk }
                    : new UserSessionOutput
                    {
                        staff_pk = session.staff_pk,
                        company_pk = session.company_pk,
                        company_code = session.company_code,
                        company_name = session.company_name,
                        branch_pk = session.branch_pk,
                        branch_code = session.branch_code,
                        branch_name = session.branch_name,
                        dept_pk = session.dept_pk,
                        dept_code = session.dept_code,
                        dept_name = session.dept_name
                    }
            };
        }

        [HttpPost]
        [Route("changePassword")]
        public async Task<JsonResponse> ChangePassword([FromBody] UserChangePasswordInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.oldPassword)
                || string.IsNullOrWhiteSpace(input.newPassword)
                || string.IsNullOrWhiteSpace(input.confirmPassword))
            {
                return new JsonResponse(false, "All password fields are required.");
            }

            if (input.newPassword != input.confirmPassword)
            {
                return new JsonResponse(false, "New passwords do not match.");
            }

            if (input.newPassword.Length < 6)
            {
                return new JsonResponse(false, "New password must be at least 6 characters.");
            }

            var staffPk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            if (string.IsNullOrWhiteSpace(staffPk))
            {
                return new JsonResponse(false, "Login is invalid, please login again.");
            }

            var staff = await _appSqlServerRepository.QueryFirstOrDefaultAsync<GlbStaffLoginRecord>(
                @"SELECT TOP 1
                         GS_PK AS gs_pk,
                         GS_LoginName AS gs_loginname,
                         GS_PasswordHash AS gs_passwordhash,
                         GS_PasswordSalt AS gs_passwordsalt,
                         GS_PasswordHashIterations AS gs_passwordhashiterations
                  FROM GlbStaff
                  WHERE GS_PK = @staffPk
                    AND ISNULL(GS_CanLogin, 0) = 1
                    AND ISNULL(GS_IsActive, 0) = 1",
                new { staffPk });

            if (staff == null || !_passwordHashService.Verify(
                    input.oldPassword,
                    staff.gs_passwordhash,
                    staff.gs_passwordsalt,
                    staff.gs_passwordhashiterations))
            {
                return new JsonResponse(false, "Current password is incorrect.");
            }

            var password = _passwordHashService.Create(input.newPassword);
            var parameters = new DynamicParameters();
            parameters.Add("staffPk", staffPk);
            parameters.Add("passwordHash", password.Hash);
            parameters.Add("passwordSalt", password.Salt);
            parameters.Add("iterations", password.Iterations);

            await _appSqlServerRepository.ExecuteAsync(
                @"UPDATE GlbStaff
                  SET GS_PasswordHash = @passwordHash,
                      GS_PasswordSalt = @passwordSalt,
                      GS_PasswordHashIterations = @iterations
                  WHERE GS_PK = @staffPk",
                parameters);

            return new JsonResponse();
        }

        private sealed class GlbStaffLoginRecord
        {
            public string gs_pk { get; set; }
            public string gs_loginname { get; set; }
            public string gs_fullname { get; set; }
            public string gs_emailaddress { get; set; }
            public byte[] gs_passwordhash { get; set; }
            public byte[] gs_passwordsalt { get; set; }
            public int gs_passwordhashiterations { get; set; }
            public string gs_gb_homebranch { get; set; }
            public string gs_ge_homedepartment { get; set; }
            public string gs_gc_homecompany { get; set; }
        }

        private sealed class UserSessionRecord
        {
            public string staff_pk { get; set; }
            public string company_pk { get; set; }
            public string company_code { get; set; }
            public string company_name { get; set; }
            public string branch_pk { get; set; }
            public string branch_code { get; set; }
            public string branch_name { get; set; }
            public string dept_pk { get; set; }
            public string dept_code { get; set; }
            public string dept_name { get; set; }
        }

        private sealed class UserSwitchOptionRecord
        {
            public string company_pk { get; set; }
            public string company_code { get; set; }
            public string company_name { get; set; }
            public string branch_pk { get; set; }
            public string branch_code { get; set; }
            public string branch_name { get; set; }
        }

        private ClaimsIdentity CreateClaimsIdentity(string userId, string userName,
            string tenantId,
            string glbStaffPk,
            string companyPk,
            string branchPk,
            string deptPk)
        {
            var claims = new List<Claim>
            {
                new Claim(AbpClaimTypes.UserId, userId),
                new Claim(AbpClaimTypes.UserName, string.IsNullOrWhiteSpace(userName) ? string.Empty : userName),
                new Claim(AbpClaimTypes.TenantId, tenantId),
                new Claim(SntClaimTypes.GlbStaffPk, glbStaffPk)
            };
            AddOptionalClaim(claims, SntClaimTypes.CompanyPk, companyPk);
            AddOptionalClaim(claims, SntClaimTypes.BranchPk, branchPk);
            AddOptionalClaim(claims, SntClaimTypes.DeptPk, deptPk);
            var claimsIdentity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(claimsIdentity);

            var identity = principal.Identity as ClaimsIdentity;
            return identity;
        }

        private static void AddOptionalClaim(List<Claim> claims, string type, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                claims.Add(new Claim(type, value));
        }
        private string CreateAccessToken(IEnumerable<Claim> claims, TimeSpan? expiration = null)
        {
            var now = DateTime.UtcNow;

            var jwtSecurityToken = new JwtSecurityToken(
                issuer: _configuration.Issuer,
                audience: _configuration.Audience,
                claims: claims,
                notBefore: now,
                expires: now.Add(expiration ?? _configuration.Expiration),
                signingCredentials: _configuration.SigningCredentials
            );

            return new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
        }

        private static List<Claim> CreateJwtClaims(ClaimsIdentity identity)
        {
            var claims = identity.Claims.ToList();
            var nameIdClaim = claims.First(c => c.Type == ClaimTypes.NameIdentifier);

            // Specifically add the jti (random nonce), iat (issued timestamp), and sub (subject/user) claims.
            claims.AddRange(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, nameIdClaim.Value),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.Now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            });

            return claims;
        }

        private string GetEncrpyedAccessToken(string accessToken)
        {
            return SimpleStringCipher.Instance.Encrypt(accessToken, AppConsts.DefaultPassPhrase);
        }
        #endregion
    }
}
