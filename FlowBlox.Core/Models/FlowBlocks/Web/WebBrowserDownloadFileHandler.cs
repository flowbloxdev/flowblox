using FlowBlox.Core.Util.Downloads;
using OpenQA.Selenium;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FlowBlox.Core.Logging;

namespace FlowBlox.Core.Models.FlowBlocks.WebBrowser
{
    internal sealed class WebBrowserDownloadFileHandler
    {
        private readonly IWebDriver _driver;
        private readonly int _timeoutSeconds;

        private readonly TransferProgressReporter? _progress;
        private readonly CancellationToken _cancellationToken;

        public WebBrowserDownloadFileHandler(
            IWebDriver driver,
            int timeoutSeconds,
            TransferProgressReporter? progress = null,
            CancellationToken cancellationToken = default)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            if (timeoutSeconds < 0)
                throw new ArgumentException("Timeout seconds must be greater than or equal to 0.", nameof(timeoutSeconds));
            _timeoutSeconds = timeoutSeconds;
            _progress = progress;
            _cancellationToken = cancellationToken;
        }

        public WebBrowserDownloadActionResult DownloadFile(
            string selector,
            WebEventSelectionMode mode,
            WebDownloadMode downloadMode,
            string downloadPath,
            string downloadDirectory)
        {
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (downloadMode == WebDownloadMode.BrowserNative)
                    return DownloadFileByBrowserClick(selector, mode, downloadDirectory);

                if (downloadMode == WebDownloadMode.HttpRequest)
                    return DownloadFileByHttpRequest(selector, mode, downloadPath);

                // Auto mode:
                if (!string.IsNullOrWhiteSpace(downloadDirectory))
                {
                    var browserResult = DownloadFileByBrowserClick(selector, mode, downloadDirectory);
                    if (browserResult.Success)
                        return browserResult;

                    if (string.IsNullOrWhiteSpace(downloadPath))
                        return browserResult;
                }

                return DownloadFileByHttpRequest(selector, mode, downloadPath);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (NoSuchElementException ex)
            {
                return new WebBrowserDownloadActionResult { Success = false, Status = WebBrowserActionStatus.ElementNotFound, Exception = ex };
            }
            catch (ElementNotInteractableException ex)
            {
                return new WebBrowserDownloadActionResult { Success = false, Status = WebBrowserActionStatus.ElementNotApplicable, Exception = ex };
            }
            catch (StaleElementReferenceException ex)
            {
                return new WebBrowserDownloadActionResult { Success = false, Status = WebBrowserActionStatus.ElementStale, Exception = ex };
            }
            catch (Exception ex)
            {
                return new WebBrowserDownloadActionResult { Success = false, Status = WebBrowserActionStatus.UnknownError, Exception = ex };
            }
        }

        private WebBrowserDownloadActionResult DownloadFileByHttpRequest(string selector, WebEventSelectionMode mode, string downloadPath)
        {
            if (string.IsNullOrWhiteSpace(downloadPath))
            {
                return new WebBrowserDownloadActionResult
                {
                    Success = false,
                    Status = WebBrowserActionStatus.FileDownloadFailed
                };
            }

            var element = FindElement(selector, mode);
            var candidateUrl = element.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(candidateUrl))
                candidateUrl = element.GetAttribute("src");

            if (string.IsNullOrWhiteSpace(candidateUrl))
            {
                return new WebBrowserDownloadActionResult
                {
                    Success = false,
                    Status = WebBrowserActionStatus.DownloadUrlMissing
                };
            }

            if (!TryCreateAbsoluteUri(candidateUrl, out var fileUri))
            {
                return new WebBrowserDownloadActionResult
                {
                    Success = false,
                    Status = WebBrowserActionStatus.DownloadUrlMissing
                };
            }

            var targetFilePath = ResolveDownloadTargetPath(downloadPath, fileUri);
            try
            {
                var targetDirectory = Path.GetDirectoryName(targetFilePath);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    return new WebBrowserDownloadActionResult
                    {
                        Success = false,
                        Status = WebBrowserActionStatus.FileDownloadFailed
                    };
                }

                Directory.CreateDirectory(targetDirectory);
                DownloadFileWithBrowserCookies(fileUri, targetFilePath);

                return new WebBrowserDownloadActionResult
                {
                    Success = true,
                    DownloadPath = targetFilePath
                };
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new WebBrowserDownloadActionResult
                {
                    Success = false,
                    Status = WebBrowserActionStatus.FileDownloadFailed,
                    Exception = ex
                };
            }
        }

        private WebBrowserDownloadActionResult DownloadFileByBrowserClick(string selector, WebEventSelectionMode mode, string downloadDirectory)
        {
            var resolvedDirectory = ResolveNativeDownloadDirectory(downloadDirectory, out var shouldConfigureBrowserDownloadDirectory);
            if (string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                return new WebBrowserDownloadActionResult
                {
                    Success = false,
                    Status = WebBrowserActionStatus.FileDownloadFailed
                };
            }

            Directory.CreateDirectory(resolvedDirectory);
            if (shouldConfigureBrowserDownloadDirectory)
                TryConfigureDownloadDirectoryForNativeBrowserDownload(resolvedDirectory);

            var knownFiles = new HashSet<string>(
                Directory.GetFiles(resolvedDirectory)
                    .Select(Path.GetFileName),
                StringComparer.OrdinalIgnoreCase);

            var element = FindElement(selector, mode);
            _progress?.Report("Browser download");
            element.Click();

            var timeoutAt = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
            while (DateTime.UtcNow <= timeoutAt)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var completedFiles = Directory.GetFiles(resolvedDirectory)
                    .Where(IsCompletedDownloadFile)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();

                var newest = completedFiles.FirstOrDefault(path => !knownFiles.Contains(Path.GetFileName(path)));
                if (!string.IsNullOrWhiteSpace(newest))
                {
                    return new WebBrowserDownloadActionResult
                    {
                        Success = true,
                        DownloadPath = newest
                    };
                }

                ReportBrowserDownloadProgress(resolvedDirectory, knownFiles);
                _cancellationToken.WaitHandle.WaitOne(250);
            }

            return new WebBrowserDownloadActionResult
            {
                Success = false,
                Status = WebBrowserActionStatus.FileDownloadFailed
            };
        }

        private void ReportBrowserDownloadProgress(string directory, HashSet<string> knownFiles)
        {
            if (_progress == null)
                return;

            long received = 0;
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (knownFiles.Contains(Path.GetFileName(path)))
                    continue;

                try
                {
                    received += new FileInfo(path).Length;
                }
                catch (IOException)
                {
                    // The browser may rename or remove a temporary file during polling.
                }
            }

            _progress.ReportBytes(received, null);
        }

        private string ResolveNativeDownloadDirectory(string configuredDownloadDirectory, out bool shouldConfigureBrowserDownloadDirectory)
        {
            shouldConfigureBrowserDownloadDirectory = false;

            if (!string.IsNullOrWhiteSpace(configuredDownloadDirectory))
            {
                shouldConfigureBrowserDownloadDirectory = true;
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredDownloadDirectory).Trim());
            }

            if (TryGetBrowserConfiguredDownloadDirectory(out var browserConfiguredDirectory))
                return browserConfiguredDirectory;

            var userProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfileDirectory))
                return null;

            return Path.Combine(userProfileDirectory, "Downloads");
        }

        private bool TryGetBrowserConfiguredDownloadDirectory(out string downloadDirectory)
        {
            downloadDirectory = null;
            if (_driver is not OpenQA.Selenium.IHasCapabilities hasCapabilities)
                return false;

            var chromeOptions = hasCapabilities.Capabilities?.GetCapability("goog:chromeOptions");
            if (!TryGetDictionaryValue(chromeOptions, "prefs", out var prefsValue)
                || !TryGetDictionaryValue(prefsValue, "download.default_directory", out var downloadDirectoryValue))
            {
                return false;
            }

            var downloadDirectoryString = downloadDirectoryValue?.ToString();
            if (string.IsNullOrWhiteSpace(downloadDirectoryString))
                return false;

            downloadDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(downloadDirectoryString).Trim());
            return true;
        }

        private static bool TryGetDictionaryValue(object source, string key, out object value)
        {
            value = null;

            if (source is Dictionary<string, object> dictionary)
                return dictionary.TryGetValue(key, out value);

            if (source is IReadOnlyDictionary<string, object> readOnlyDictionary)
                return readOnlyDictionary.TryGetValue(key, out value);

            return false;
        }

        private IWebElement FindElement(string selector, WebEventSelectionMode mode)
        {
            return mode == WebEventSelectionMode.XPath
                ? _driver.FindElement(By.XPath(selector))
                : _driver.FindElement(By.CssSelector(selector));
        }

        private bool TryCreateAbsoluteUri(string candidateUrl, out Uri absoluteUri)
        {
            absoluteUri = null;
            if (string.IsNullOrWhiteSpace(candidateUrl))
                return false;

            if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var parsedAbsolute))
            {
                absoluteUri = parsedAbsolute;
                return true;
            }

            var currentUrl = _driver.Url;
            if (string.IsNullOrWhiteSpace(currentUrl))
                return false;

            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
                return false;

            absoluteUri = new Uri(currentUri, candidateUrl);
            return true;
        }

        private static string ResolveDownloadTargetPath(string downloadPath, Uri fileUri)
        {
            var resolvedInput = Environment.ExpandEnvironmentVariables(downloadPath).Trim();
            var useAsDirectory =
                resolvedInput.EndsWith(Path.DirectorySeparatorChar)
                || resolvedInput.EndsWith(Path.AltDirectorySeparatorChar)
                || Directory.Exists(resolvedInput);

            if (!useAsDirectory && Path.HasExtension(resolvedInput))
                return Path.GetFullPath(resolvedInput);

            var fileName = Path.GetFileName(fileUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "download.bin";

            return Path.GetFullPath(Path.Combine(resolvedInput, fileName));
        }

        private void DownloadFileWithBrowserCookies(Uri fileUri, string targetFilePath)
        {
            var cookieContainer = new CookieContainer();
            foreach (var cookie in _driver.Manage().Cookies.AllCookies)
            {
                try
                {
                    cookieContainer.Add(new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Failed to map browser cookie '{cookie.Name}' to a .NET cookie.",
                        ex);
                }
            }

            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = cookieContainer,
                AllowAutoRedirect = true
            };

            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FlowBlox", "1.0"));
            DownloadStreamAsync(client, fileUri, targetFilePath).GetAwaiter().GetResult();
        }

        private async Task DownloadStreamAsync(HttpClient client, Uri fileUri, string targetFilePath)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
            if (_timeoutSeconds > 0)
                timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            var cancellationToken = timeout.Token;
            var temporaryPath = targetFilePath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                _progress?.Report("Downloading");
                using var response = await client.GetAsync(
                    fileUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    var total = response.Content.Headers.ContentLength;
                    int count;
                    while ((count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        received += count;
                        _progress?.ReportBytes(received, total);
                    }

                    if (total.HasValue && received != total.Value)
                        throw new IOException("The HTTP download ended before the expected content length was received.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, targetFilePath, overwrite: true);
            }
            catch (OperationCanceledException) when (!_cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new TimeoutException("The HTTP download exceeded its configured timeout.");
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static bool IsCompletedDownloadFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return !string.Equals(extension, ".crdownload", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".part", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase);
        }

        private void TryConfigureDownloadDirectoryForNativeBrowserDownload(string downloadDirectory)
        {
            if (_driver is OpenQA.Selenium.Chrome.ChromeDriver chromeDriver)
            {
                chromeDriver.ExecuteCdpCommand("Page.setDownloadBehavior", new Dictionary<string, object>
                {
                    { "behavior", "allow" },
                    { "downloadPath", downloadDirectory }
                });
            }
        }
    }
}
