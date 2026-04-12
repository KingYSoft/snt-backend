using Abp.AspNetCore;
using Abp.AspNetCore.Localization;
using Abp.Extensions;
using Facade.AspNetCore;
using SntBackend.Web.Core.Authentication;
using SntBackend.Web.Core.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using SntBackend.Web.Core.AspNetCore.Builders;

namespace SntBackend.Web.Core.AspNetCore
{
    public static class SntBackendServiceCollectionExtensions
    {
        private const string _defaultCorsPolicyName = "localhost";

        /// <summary>
        /// 配置SntBackend服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="appConfiguration"></param>
        public static void ConfigureSntBackendService(this IServiceCollection services, IConfigurationRoot appConfiguration)
        {
            services.AddControllers(options =>
            {
                //query?xx=xx
                options.ModelBinderProviders.Insert(0, new TrimStringModelBinderProvider());
                //obj
                options.Filters.Add<TrimStringActionFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new NumberToStringJsonConverter());
            });
            // services.AddControllers();

            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.SuppressConsumesConstraintForFormFileParameters = true;
                options.SuppressInferBindingSourcesForParameters = true;
                options.SuppressModelStateInvalidFilter = true;
            });

            AuthConfigurer.Configure(services, appConfiguration);

            services.Configure<MvcOptions>((op) =>
            {
                // enable debounce
                op.Filters.AddService(typeof(DebounceActionFilter));
            });

            services.AddSignalR();

            // Configure CORS for angular2 UI
            services.AddCors(
                options => options.AddPolicy(
                    _defaultCorsPolicyName,
                    builder => builder
                        .WithOrigins(
                            // App:CorsOrigins in appsettings.json can contain more than one address separated by comma.
                            appConfiguration["App:CorsOrigins"]
                                .Split(",", StringSplitOptions.RemoveEmptyEntries)
                                .Select(o => o.RemovePostFix("/"))
                                .ToArray()
                        )
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials()
                )
            );
        }

        /// <summary>
        /// 配置SntBackend服务
        /// </summary>
        /// <param name="app"></param>
        /// <param name="appConfiguration"></param>
        public static void ConfigureSntBackendApp(this IApplicationBuilder app, IConfigurationRoot appConfiguration)
        {
            app.UseFacade(options => { options.UseAbpRequestLocalization = false; }); // Initializes ABP framework.

            //app.UseStaticFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = (c) =>
                {
                    // 资源文件跨域
                    c.Context.Response.Headers.AccessControlAllowOrigin = "*";
                }
            });
            app.UseRouting();
            app.UseCors(_defaultCorsPolicyName); // Enable CORS!
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseAbpRequestLocalization(options =>
            {
                var logger = app.ApplicationServices.GetRequiredService<Castle.Core.Logging.ILogger>();
                var headerProvider = options.RequestCultureProviders.OfType<AbpLocalizationHeaderRequestCultureProvider>().FirstOrDefault();
                if (headerProvider != null)
                {
                    headerProvider.HeaderName = "Facade-Language";
                    headerProvider.Logger = logger;
                }
                var defaultProvider = options.RequestCultureProviders.OfType<AbpDefaultRequestCultureProvider>().FirstOrDefault();
                if (defaultProvider != null)
                {
                    defaultProvider.Logger = logger;
                }
                var userProvider = options.RequestCultureProviders.OfType<AbpUserRequestCultureProvider>().FirstOrDefault();
                if (userProvider != null)
                {
                    userProvider.Logger = logger;
                }
            });

            // demo middleware
            // app.UseMiddleware<DemoMiddleware>();
        }
    }
}
