using Abp.Dependency;
using Abp.Extensions;
using Abp.Reflection.Extensions;
using Castle.Facilities.Logging;
using Castle.MicroKernel.ModelBuilder.Inspectors;
using Castle.MicroKernel.SubSystems.Conversion;
using Facade.AspNetCore;
using Facade.AspNetCore.Configuration;
using Facade.Core.Configuration;
using Facade.NLogger;
using SntBackend.Application;
using SntBackend.DomainService;
using SntBackend.DomainService.Share;
using SntBackend.SqlServer;
using SntBackend.Web.Core.AspNetCore;
using SntBackend.Web.Core.Swagger;
using SntBackend.Web.Host.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SntBackend.Web.Host
{
    public class Startup
    {
        private readonly IConfigurationRoot _appConfiguration;
        private readonly IWebHostEnvironment _env;

        public Startup(IWebHostEnvironment env)
        {
            _env = env;
            _appConfiguration = env.BuildConfiguration(new FacadeConfigurationBuilderOptions()
            {
                UserSecretsAssembly = typeof(SntBackendWebHostModule).GetAssembly()
            });
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.ConfigureSntBackendService(_appConfiguration);

            if (_env.IsDevelopment() || _env.EnvironmentName.Equals("Test", StringComparison.OrdinalIgnoreCase))
            {
                // Swagger - Enable this line and the related lines in Configure method to enable swagger UI
                services.AddSwaggerGen(options =>
                {
                    options.ConfigureSntBackend();

                    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SntBackend API", Version = "v1" });
                    options.DocInclusionPredicate((docName, description) =>
                    {
                        description.TryGetMethodInfo(out MethodInfo mi);

                        var attr = mi.DeclaringType.GetCustomAttribute<ApiExplorerSettingsAttribute>();
                        if (attr != null)
                        {
                            if (docName == "v1")
                            {
                                return true;
                            }
                            else
                            {
                                return attr.GroupName == docName;
                            }
                        }
                        else
                        {
                            return docName == "v1";
                        }
                    });

                    var types = new Type[]
                    {
                        typeof(SntBackendWebHostModule),
                        typeof(SntBackendApplicationModule),
                        typeof(SntBackendDomainServiceModule),
                        typeof(SntBackendDomainServiceShareModule),
                        typeof(SntBackendSqlServerModule),
                    };
                    foreach (var t in types)
                    {
                        var xmlFile = $"{t.GetAssembly().GetName().Name}.xml";
                        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                        options.IncludeXmlComments(xmlPath, true);
                    }
                });
            }
            // Configure Abp and Dependency Injection
            return services.AddFacade<SntBackendWebHostModule>(
                // Configure Log4Net logging
                options =>
                {
                    options.IocManager.IocContainer.AddFacility<LoggingFacility>(f =>
                    {
                        f.UseFacadeNLog($"{_env.ContentRootPath}\\NLog.config");
                    });
                    var propInjector = options.IocManager.IocContainer.Kernel.ComponentModelBuilder
                    .Contributors
                    .OfType<PropertiesDependenciesModelInspector>()
                    .Single();

                    options.IocManager.IocContainer.Kernel.ComponentModelBuilder.RemoveContributor(propInjector);
                    options.IocManager.IocContainer.Kernel.ComponentModelBuilder.AddContributor(new AbpPropertiesDependenciesModelInspector(new DefaultConversionManager()));
                });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            app.ConfigureSntBackendApp(_appConfiguration);

            app.UseEndpoints(c =>
            {
                c.MapHub<SntBackendHub>("/signalr");
                c.MapControllers();
            });
            if (_env.IsDevelopment() || _env.EnvironmentName.Equals("Test", StringComparison.OrdinalIgnoreCase))
            {
                // Enable middleware to serve generated Swagger as a JSON endpoint
                app.UseSwagger(v =>
                {
                    v.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi2_0;
                });
                // Enable middleware to serve swagger-ui assets (HTML, JS, CSS etc.)
                app.UseSwaggerUI(options =>
                {
                    options.ConfigureUISntBackend();
                    options.IndexStream = () =>
                    {
                        return Assembly.GetExecutingAssembly().GetManifestResourceStream("SntBackend.Web.Host.wwwroot.swagger.ui.index.html");
                    };
                    options.SwaggerEndpoint(_appConfiguration["App:ServerRootAddress"].EnsureEndsWith('/') + "swagger/v1/swagger.json", "SntBackend API V1");

                }); // URL: /swagger
            }
        }
    }
}
