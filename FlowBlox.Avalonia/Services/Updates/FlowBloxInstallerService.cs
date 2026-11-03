using FlowBlox.Core.Logging;
using FlowBlox.AvaloniaDesktop.Services;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FlowBlox.AvaloniaDesktop.Services.Updates
{
    public class FlowBloxInstallerService : IFlowBloxInstallerService
    {
        private readonly HttpClient _httpClient;

        public FlowBloxInstallerService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<InstallerUpdateInfo> GetLatestInstallerUpdateAsync(string manifestUrl)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
                return null;

            try
            {
                var xml = await _httpClient.GetStringAsync(manifestUrl);
                var doc = XDocument.Parse(xml);

                var root = doc.Root;
                if (root == null || !string.Equals(root.Name.LocalName, "FlowBloxInstallerUpdate", StringComparison.OrdinalIgnoreCase))
                    return null;

                var versionText = root.Element("LatestVersion")?.Value?.Trim();
                var installerUrl = root.Element("InstallerUrl")?.Value?.Trim();

                if (string.IsNullOrWhiteSpace(versionText) || string.IsNullOrWhiteSpace(installerUrl))
                    return null;

                if (!Version.TryParse(versionText, out var version))
                    return null;

                return new InstallerUpdateInfo(version, installerUrl);
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to load installer update manifest.", ex);
                return null;
            }
        }

        public async Task<string> DownloadInstallerAsync(string installerUrl, string updateDownloadDirectoryName)
        {
            if (string.IsNullOrWhiteSpace(installerUrl))
                return null;

            if (string.IsNullOrWhiteSpace(updateDownloadDirectoryName))
                return null;

            try
            {
                var uri = new Uri(installerUrl);
                var fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName))
                    return null;

                var updateDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlowBlox",
                    updateDownloadDirectoryName);

                if (!Directory.Exists(updateDir))
                    Directory.CreateDirectory(updateDir);

                var targetPath = Path.Combine(updateDir, fileName);
                var data = await _httpClient.GetByteArrayAsync(uri);
                await File.WriteAllBytesAsync(targetPath, data);
                return targetPath;
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to download installer update.", ex);
                return null;
            }
        }
    }
}
