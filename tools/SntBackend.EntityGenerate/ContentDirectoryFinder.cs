using Abp.Reflection.Extensions;
using System;
using System.IO;
using System.Linq;

namespace SntBackend.EntityGenerate
{
    public static class ContentDirectoryFinder
    {
        public static string CalculateProjectFolder(string projectName)
        {
            var coreAssemblyDirectoryPath = Path.GetDirectoryName(typeof(SntBackendEntityGenerateModule).GetAssembly().Location);
            if (coreAssemblyDirectoryPath == null)
            {
                throw new Exception("找不到当前程序集下的路径！");
            }

            var directoryInfo = new DirectoryInfo(coreAssemblyDirectoryPath);
            while (!DirectoryContains(directoryInfo.FullName, "SntBackend.sln"))
            {
                if (directoryInfo.Parent == null)
                {
                    throw new Exception("找不到根目录！");
                }

                directoryInfo = directoryInfo.Parent;
            }

            var folder = Path.Combine(directoryInfo.FullName, "src", projectName);
            if (Directory.Exists(folder))
            {
                return folder;
            }

            throw new Exception($"找不到这个项目的{projectName}目录");
        }

        private static bool DirectoryContains(string directory, string fileName)
        {
            return Directory.GetFiles(directory).Any(filePath => string.Equals(Path.GetFileName(filePath), fileName));
        }
    }
}
