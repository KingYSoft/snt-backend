using Abp.Dependency;

namespace SntBackend.DomainService.Folders
{
    public class AppFolders : IAppFolders, ISingletonDependency
    {
        public string TempFileUploadFolder { get; set; }
        public string TempFileDownloadFolder { get; set; }
        public string FileUploadFolder { get; set; }
    }
}
