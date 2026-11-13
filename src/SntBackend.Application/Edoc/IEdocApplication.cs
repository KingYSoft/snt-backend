using System.IO;
using System.Threading.Tasks;
using SntBackend.Application.Edoc.Dto;

namespace SntBackend.Application.Edoc
{
    public interface IEdocApplication : ISntBackendApplicationBase
    {
        Task<EdocSearchOutput> Search(EdocSearchInput input);

        Task<EdocItemOutput> Save(
            EdocSaveInput input,
            Stream file,
            string fileName,
            string contentType,
            long fileLength);
    }
}
