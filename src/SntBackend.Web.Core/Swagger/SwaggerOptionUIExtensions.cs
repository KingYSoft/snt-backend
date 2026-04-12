using Microsoft.AspNetCore.Builder;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace SntBackend.Web.Core.Swagger
{
    public static class SwaggerOptionUIExtensions
    {
        /// <summary>
        /// 配置 Swagger Options UI
        /// </summary>
        /// <param name="options"></param>
        public static void ConfigureUISntBackend(this SwaggerUIOptions options)
        {
            options.DisplayRequestDuration();
        }
    }
}
