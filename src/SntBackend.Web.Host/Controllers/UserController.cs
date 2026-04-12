using Abp.Runtime.Security;
using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using SntBackend.Application;
using SntBackend.Web.Core.Authentication.JwtBearer;
using SntBackend.Web.Core.Controllers;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Abp.Auditing;
using SntBackend.Application.User.Dto;

namespace SntBackend.Web.Host.Controllers
{
    [Route("user")]
    public class UserController : SntBackendControllerBase
    {
        private readonly TokenAuthConfiguration _configuration;
        public UserController(TokenAuthConfiguration configuration)
        {
            _configuration = configuration;
        }
        #region login
        [HttpPost]
        [NoToken]
        [Route("login")]
        [DisableAuditing]
        public async Task<JsonResponse<UserLoginOutput>> Login([FromBody] UserLoginInput input)
        {
            await Task.CompletedTask;
            if (input.email == "admin" && input.password == "123456")
            {
                var identity = CreateClaimsIdentity($"1", input.email, string.Empty);
                var accessToken = GetEncrpyedAccessToken(CreateAccessToken(CreateJwtClaims(identity)));

                return new JsonResponse<UserLoginOutput>
                {
                    Data = new UserLoginOutput
                    {
                        access_token = accessToken,
                        full_name = input.email,
                        email_address = input.email,
                        login_name = input.email
                    }
                };
            }
            else
            {
                return new JsonResponse<UserLoginOutput>(false, "login fail.");
            }
        }
        private ClaimsIdentity CreateClaimsIdentity(string userId, string userName,
            string tenantId)
        {
            var claims = new List<Claim>
            {
                new Claim(AbpClaimTypes.UserId, userId),
                new Claim(AbpClaimTypes.UserName, string.IsNullOrWhiteSpace(userName) ? string.Empty : userName),
                new Claim(AbpClaimTypes.TenantId, tenantId)
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
