using Abp.Dependency;
using Facade;
using Microsoft.Playwright;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SntBackend.Application.Pdf
{
    /// <summary>
    /// 用 Playwright 无头 Chromium 把 HTML 渲染成 PDF。浏览器实例进程内复用。
    /// 宿主机需预先执行 `playwright install chromium`（或 `dotnet ... playwright.ps1 install chromium`）。
    /// </summary>
    public class PlaywrightPdfGenerator : ISingletonDependency
    {
        private const float OperationTimeoutMilliseconds = 60000;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private IPlaywright _playwright;
        private IBrowser _browser;

        /// <summary>
        /// 把 HTML 渲染为 PDF 并写到指定物理路径。
        /// </summary>
        public async Task GenerateAsync(string html, string fullPath)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var browser = await GetBrowserAsync();
                var page = await browser.NewPageAsync();
                try
                {
                    await page.SetContentAsync(html, new PageSetContentOptions
                    {
                        WaitUntil = WaitUntilState.Load,
                        Timeout = OperationTimeoutMilliseconds
                    });

                    await page.PdfAsync(new PagePdfOptions
                    {
                        Path = fullPath,
                        Format = "A4",
                        PrintBackground = true,
                        Margin = new Margin
                        {
                            Top = "4mm",
                            Right = "4mm",
                            Bottom = "4mm",
                            Left = "4mm"
                        }
                    });
                }
                finally
                {
                    await page.CloseAsync();
                }
            }
            catch (PlaywrightException ex)
            {
                throw new AppException($"playwright pdf generate failed: {ex.Message}. 请在宿主机执行 'playwright install chromium'。");
            }
        }

        /// <summary>
        /// 懒加载并复用同一个无头浏览器实例。
        /// </summary>
        private async Task<IBrowser> GetBrowserAsync()
        {
            if (_browser != null)
            {
                return _browser;
            }

            await _initLock.WaitAsync();
            try
            {
                if (_browser != null)
                {
                    return _browser;
                }

                _playwright ??= await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });

                return _browser;
            }
            finally
            {
                _initLock.Release();
            }
        }
    }
}
