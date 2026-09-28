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
                isSystemAdmin ? null : staff.gs_ge_homedepartment);
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
                    dept_pk = isSystemAdmin ? null : staff.gs_ge_homedepartment
                }
            };
        }

        [HttpPost]
        [Route("switch-branch")]
        public async Task<JsonResponse<UserLoginOutput>> SwitchBranch([FromBody] UserSwitchBranchInput input)
        {
            var staffPk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value;
            var deptPk = input?.dept_pks?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
            if (string.IsNullOrWhiteSpace(staffPk) || string.IsNullOrWhiteSpace(deptPk))
                return new JsonResponse<UserLoginOutput>(false, "Select a department.");

            var scope = await _businessDataPermissionService.GetAccessibleDepartmentAsync(staffPk, deptPk);
            if (scope == null)
                return new JsonResponse<UserLoginOutput>(false, "The selected department is not accessible.");

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

            await _permissionCache.ClearAsync(staff.gs_pk);
            var isSystemAdmin = (await _permissionCache.GetGrantedNamesAsync(staff.gs_pk)).Contains("*");

            var identity = CreateClaimsIdentity(
                "1",
                staff.gs_loginname,
                string.Empty,
                staff.gs_pk,
                isSystemAdmin ? null : scope.company_pk,
                isSystemAdmin ? null : scope.branch_pk,
                isSystemAdmin ? null : scope.dept_pk);
            var accessToken = GetEncrpyedAccessToken(CreateAccessToken(CreateJwtClaims(identity)));

            return new JsonResponse<UserLoginOutput>
            {
                Data = new UserLoginOutput
                {
                    accessToken = accessToken,
                    full_name = staff.gs_fullname,
                    email_address = staff.gs_emailaddress,
                    login_name = staff.gs_loginname,
                    company_pk = isSystemAdmin ? null : scope.company_pk,
                    branch_pk = isSystemAdmin ? null : scope.branch_pk,
                    dept_pk = isSystemAdmin ? null : scope.dept_pk
                }
            };
        }

        [HttpGet]
        [Route("session")]
        public JsonResponse<UserSessionOutput> Session()
        {
            return new JsonResponse<UserSessionOutput>
            {
                Data = new UserSessionOutput
                {
                    staff_pk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.GlbStaffPk)?.Value,
                    company_pk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.CompanyPk)?.Value,
                    branch_pk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.BranchPk)?.Value,
                    dept_pk = User?.Claims?.FirstOrDefault(x => x.Type == SntClaimTypes.DeptPk)?.Value
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
