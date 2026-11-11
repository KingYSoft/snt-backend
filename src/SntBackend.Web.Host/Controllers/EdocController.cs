using Facade.Core.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SntBackend.Application.Edoc;
using SntBackend.Application.Edoc.Dto;
using SntBackend.Web.Core.Controllers;
using System.Threading.Tasks;

namespace SntBackend.Web.Host.Controllers
{
    [Route("edoc")]
    public class EdocController : SntBackendControllerBase
    {
        private readonly IEdocApplication _edocApplication;

        public EdocController(IEdocApplication edocApplication)
        {
            _edocApplication = edocApplication;
        }

        [HttpPost]
        [Route("search")]
        public async Task<JsonResponse<EdocSearchOutput>> Search([FromBody] EdocSearchInput input)
        {
            return new JsonResponse<EdocSearchOutput>
            {
                Data = await _edocApplication.Search(input)
            };
        }

        [HttpPost]
        [Route("save")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(101 * 1024 * 1024)]
        public async Task<JsonResponse<EdocItemOutput>> Save(
            IFormFile file,
            [FromForm] string parent_table,
            [FromForm] string related_key,
            [FromForm] string file_type,
            [FromForm] string ref_no,
            [FromForm] int? is_ebooking_visible)
        {
            if (file == null)
                throw new Facade.AppException("file can not null.");

            await using var stream = file.OpenReadStream();
            return new JsonResponse<EdocItemOutput>
            {
                Data = await _edocApplication.Save(
                    new EdocSaveInput
                    {
                        parent_table = parent_table,
                        related_key = related_key,
                        file_type = file_type,
                        ref_no = ref_no,
                        is_ebooking_visible = is_ebooking_visible.GetValueOrDefault()
                    },
                    stream,
                    file.FileName,
                    file.ContentType,
                    file.Length)
            };
        }
    }
}
