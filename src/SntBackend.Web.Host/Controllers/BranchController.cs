using Facade.AspNetCore.Mvc.Authorization;
using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Branch;
using SntBackend.Application.SystemSettings.Dto;
using SntBackend.DomainService.Share.Authorization;
using SntBackend.Web.Core.Authorization;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers
{
    [Route("branch")]
    public class BranchController : SntBackendControllerBase
    {
        private readonly IBranchApplication _branchApplication;
        private readonly SystemPermissionChecker _permissionChecker;

        public BranchController(
            IBranchApplication branchApplication,
            SystemPermissionChecker permissionChecker)
        {
            _branchApplication = branchApplication;
            _permissionChecker = permissionChecker;
        }

        [HttpPost]
        [Route("query-page")]
        public async Task<JsonResponse<Abp.Application.Services.Dto.PagedResultDto<BranchDtoOutput>>> QueryPage(
            [FromBody] BranchQueryInput input)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemBranch);
            return new JsonResponse<Abp.Application.Services.Dto.PagedResultDto<BranchDtoOutput>>
            {
                Data = await _branchApplication.QueryPage(input)
            };
        }

        [HttpPost]
        [Route("save")]
        public async Task<JsonResponse<BranchDtoOutput>> Save([FromBody] BranchSaveInput input)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemBranch);
            return new JsonResponse<BranchDtoOutput>
            {
                Data = await _branchApplication.Save(input)
            };
        }

        [HttpPost]
        [Route("delete/{pk}")]
        public async Task<JsonResponse> Delete(string pk)
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemBranch);
            await _branchApplication.Delete(pk);
            return new JsonResponse();
        }

        [HttpPost]
        [Route("get")]
        [NoToken]
        public async Task<JsonResponse<BranchDtoOutput>> Get([FromBody] BranchGetInput input)
        {
            return new JsonResponse<BranchDtoOutput>
            {
                Data = await _branchApplication.GetByPk(input)
            };
        }

        [HttpGet]
        [Route("group-list")]
        public async Task<JsonResponse<BranchGroupListOutput>> GroupList()
        {
            await _permissionChecker.EnsureGrantedAsync(User, PermissionNameConsts.SystemBranch);
            return new JsonResponse<BranchGroupListOutput>
            {
                Data = await _branchApplication.GroupList()
            };
        }
    }
}
