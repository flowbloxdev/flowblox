using System.Threading.Tasks;
using FlowBlox.AvaloniaDesktop.Services.Updates;

namespace FlowBlox.AvaloniaDesktop.Services
{
    public interface IFlowBloxInstallerService
    {
        Task<InstallerUpdateInfo> GetLatestInstallerUpdateAsync(string manifestUrl);

        Task<string> DownloadInstallerAsync(string installerUrl, string updateDownloadDirectoryName);
    }
}
