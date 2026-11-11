using Facade.Core.Web;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Settlement.ClosingPeriod;
using SntBackend.Application.Settlement.ClosingPeriod.Dto;
using SntBackend.Web.Core.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Abp.Application.Services.Dto;

namespace SntBackend.Web.Host.Controllers
{
    [Route("sun-period-manage")]
    public class ClosingPeriodController : SntBackendControllerBase
    {
        private readonly IClosingPeriodApplication _application;

        public ClosingPeriodController(IClosingPeriodApplication application)
        {
            _application = application;
        }

        [HttpPost]
        [Route("query-page")]
        public async Task<JsonResponse<PagedResultDto<ClosingPeriodRecord>>> QueryPage([FromBody] ClosingPeriodQueryInput input)
        {
            return new JsonResponse<PagedResultDto<ClosingPeriodRecord>> { Data = await _application.QueryPage(input) };
        }

        [HttpPost]
        [Route("add")]
        public async Task<JsonResponse<List<ClosingPeriodRecord>>> Add([FromBody] ClosingPeriodSaveInput input)
        {
            return new JsonResponse<List<ClosingPeriodRecord>> { Data = await _application.Create(input) };
        }

        [HttpPost]
        [Route("update")]
        public async Task<JsonResponse<ClosingPeriodRecord>> Update([FromBody] ClosingPeriodUpdateInput input)
        {
            return new JsonResponse<ClosingPeriodRecord> { Data = await _application.Update(input) };
        }
    }
}
