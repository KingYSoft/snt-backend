using Abp.Runtime.Security;
using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using SntBackend.Application;
using SntBackend.Web.Core.Authentication.JwtBearer;
using SntBackend.Web.Core.Controllers;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Threading.Tasks;
using Abp.Auditing;
using SntBackend.Application.User.Dto;
using SntBackend.DomainService.Share.App;

namespace SntBackend.Web.Host.Controllers
{
    [Route("user")]
    public class UserController : SntBackendControllerBase
    {
        private const string GlbStaffPkClaim = "glb_staff_pk";
        private const string LoginFailedMessage = "Incorrect username or password.";
        private readonly TokenAuthConfiguration _configuration;
        private readonly IAppSqlServerRepository _appSqlServerRepository;

        public UserController(
            TokenAuthConfiguration configuration,
            IAppSqlServerRepository appSqlServerRepository)
        {
            _configuration = configuration;
            _appSqlServerRepository = appSqlServerRepository;
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
                         GS_PasswordHashIterations AS gs_passwordhashiterations
                  FROM GlbStaff
                  WHERE GS_LoginName = @loginName
                    AND ISNULL(GS_CanLogin, 0) = 1
                    AND ISNULL(GS_IsActive, 0) = 1",
                new { loginName });

            if (staff == null || !VerifyPassword(
                    input.password,
                    staff.gs_passwordhash,
                    staff.gs_passwordsalt,
                    staff.gs_passwordhashiterations))
            {
                return new JsonResponse<UserLoginOutput>(false, LoginFailedMessage);
            }

            var identity = CreateClaimsIdentity("1", staff.gs_loginname, string.Empty, staff.gs_pk);
            var accessToken = GetEncrpyedAccessToken(CreateAccessToken(CreateJwtClaims(identity)));

            return new JsonResponse<UserLoginOutput>
            {
                Data = new UserLoginOutput
                {
                    accessToken = accessToken,
                    full_name = staff.gs_fullname,
                    email_address = staff.gs_emailaddress,
                    login_name = staff.gs_loginname
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

            var staffPk = User?.Claims?.FirstOrDefault(x => x.Type == GlbStaffPkClaim)?.Value;
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

            if (staff == null || !VerifyPassword(
                    input.oldPassword,
                    staff.gs_passwordhash,
                    staff.gs_passwordsalt,
                    staff.gs_passwordhashiterations))
            {
                return new JsonResponse(false, "Current password is incorrect.");
            }

            var password = CreatePasswordHash(input.newPassword);
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

        private static bool VerifyPassword(string password, byte[] expectedHash, byte[] salt, int iterations)
        {
            if (string.IsNullOrEmpty(password) || expectedHash == null || salt == null || iterations <= 0)
                return false;

            using var derive = new Rfc2898DeriveBytes(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA1);
            var actualHash = derive.GetBytes(expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        private static (byte[] Hash, byte[] Salt, int Iterations) CreatePasswordHash(string password)
        {
            const int iterations = 200000;
            var salt = RandomNumberGenerator.GetBytes(16);
            using var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA1);
            return (derive.GetBytes(20), salt, iterations);
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
        }

        private ClaimsIdentity CreateClaimsIdentity(string userId, string userName,
            string tenantId, string glbStaffPk)
        {
            var claims = new List<Claim>
            {
                new Claim(AbpClaimTypes.UserId, userId),
                new Claim(AbpClaimTypes.UserName, string.IsNullOrWhiteSpace(userName) ? string.Empty : userName),
                new Claim(AbpClaimTypes.TenantId, tenantId),
                new Claim(GlbStaffPkClaim, glbStaffPk)
            };
            var claimsIdentity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(claimsIdentity);

            var identity = principal.Identity as ClaimsIdentity;
            return identity;
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
