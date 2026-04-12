using Abp.Application.Services;
using Abp.Application.Services.Dto;
using Abp.Dependency;

namespace SntBackend.Application
{
  public interface ISntBackendApplicationBase : IApplicationService, ITransientDependency
  {
  }
}