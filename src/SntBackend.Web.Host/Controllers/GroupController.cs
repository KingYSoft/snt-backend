using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Group;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.Authorization;
using SntBackend.Web.Core.Authorization;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers
{
    [Route("group")]
    public class GroupController : SntBackendControllerBase
    {
        private readonly IGroupApplication _groupApplication;
        private readonly SystemPermissionChecker _permissionChecker;

        public GroupController(
            IGroupApplication groupApplication,
            SystemPermissionChecker permissionChecker)
        {
            _groupApplication = groupApplication;
            _permissionChecker = permissionChecker;
        }

        [HttpGet]
        [Route("all-permission")]
        public async Task<JsonResponse<GroupAllPermissionOutput>> AllPermission()
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemGroup);
            return new JsonResponse<GroupAllPermissionOutput>
            {
                Data = await _groupApplication.AllPermission()
            };
        }

        [HttpGet]
        [Route("query-page")]
        public async Task<JsonResponse<Abp.Application.Services.Dto.PagedResultDto<GroupDtoOutput>>> QueryPage(
            [FromQuery] GroupQueryInput input)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemGroup);
            return new JsonResponse<Abp.Application.Services.Dto.PagedResultDto<GroupDtoOutput>>
            {
                Data = await _groupApplication.QueryPage(input)
            };
        }

        [HttpGet]
        [Route("detail")]
        public async Task<JsonResponse<GroupDetailOutput>> Detail([FromQuery] string pk)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemGroup);
            return new JsonResponse<GroupDetailOutput>
            {
                Data = await _groupApplication.Detail(pk)
            };
        }

        [HttpPost]
        [Route("save")]
        public async Task<JsonResponse<GroupSaveOutput>> Save([FromBody] GroupSaveInput input)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemGroup);
            return new JsonResponse<GroupSaveOutput>
            {
                Data = await _groupApplication.Save(input)
            };
        }

        [HttpGet]
        [Route("query-company-branch-dept-options")]
        public async Task<JsonResponse<CompanyBranchDeptOptionsOutput>> QueryCompanyBranchDeptOptions()
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemGroup);
            return new JsonResponse<CompanyBranchDeptOptionsOutput>
            {
                Data = await _groupApplication.QueryCompanyBranchDeptOptions()
            };
        }
    }
}
