using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SntBackend.Web.Core.Swagger
{
    public static class SwaggerOptionExtensions
    {
        /// <summary>
        /// 配置 Swagger Options
        /// </summary>
        /// <param name="options"></param>
        public static void ConfigureSntBackend(this SwaggerGenOptions options)
        {
            //options.DocumentFilter<SwaggerIgnoreFilter>();
            options.OperationFilter<SwaggerFileUploadFilter>();


            // Define the BearerAuth scheme that's in use
            options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme()
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey
            });
        }
    }
}
