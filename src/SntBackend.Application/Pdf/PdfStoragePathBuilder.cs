using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SntBackend.Application.Pdf
{
    /// <summary>
    /// PDF 落盘路径信息。
    /// </summary>
    public class PdfStoragePath
    {
        /// <summary>物理全路径。</summary>
        public string FullPath { get; set; }

        /// <summary>对外访问的相对路径，形如 /files/pdf/202608/xxx.pdf。</summary>
        public string OutputRelativePath { get; set; }
    }

    /// <summary>
    /// 生成 PDF 的存储路径：{wwwroot}/files/pdf/{yyyyMM}/{前缀}_{业务号}_{时间戳}.pdf。
    /// </summary>
    public static class PdfStoragePathBuilder
    {
        private const string OutputPrefix = "/files/pdf/";

        /// <summary>
        /// 构建一次生成的存储路径。
        /// </summary>
        /// <param name="pdfRootFolder">PDF 根目录，即 IAppFolders.FilePdfFolder（{wwwroot}/files/pdf）。</param>
        /// <param name="filePrefix">文件名前缀，例如 INV。</param>
        /// <param name="businessNo">业务号，通常是发票号。</param>
        /// <param name="now">生成时间。</param>
        public static PdfStoragePath Build(string pdfRootFolder, string filePrefix, string businessNo, DateTime now)
        {
            var folderName = now.ToString("yyyyMM");
            var fileName = $"{Sanitize(filePrefix)}_{Sanitize(businessNo)}_{now:yyyyMMddHHmmssfff}.pdf";

            return new PdfStoragePath
            {
                FullPath = Path.Combine(pdfRootFolder, folderName, fileName),
                OutputRelativePath = $"{OutputPrefix}{folderName}/{fileName}"
            };
        }

        /// <summary>
        /// 把文件名里的不安全字符替换成下划线。
        /// </summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNKNOWN";
            }

            return Regex.Replace(value.Trim(), "[^A-Za-z0-9_-]", "_");
        }
    }
}
