using Abp.AspNetCore.Mvc.Authorization;
using Abp.AspNetCore.Mvc.Controllers;
using SntBackend.DomainService.Share;
using Microsoft.AspNetCore.Mvc;

namespace SntBackend.Web.Core.Controllers
{
    [ApiController]
    [AbpMvcAuthorize]
    public abstract class SntBackendControllerBase : AbpController
    {
        protected SntBackendControllerBase()
        {
            LocalizationSourceName = SntBackendConsts.LocalizationSourceName;
        }
    }
}
