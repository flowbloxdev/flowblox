using System.Reflection;

namespace FlowBlox.Core.Constants
{
    public static class GlobalUrls
    {
        public const string FlowBloxWebsite = "https://www.flowblox.net/";
        public const string FlowBloxGitHubRepository = "https://github.com/flowbloxdev/flowblox";
        public const string FlowBloxSampleExtensionRepository = "https://github.com/flowbloxdev/flowblox/tree/main/FlowBloxSampleExtension";
        public const string FlowBloxSampleExtensionUIRepository = "https://github.com/flowbloxdev/flowblox/tree/main/FlowBloxSampleExtension.UI";
        public const string FlowBloxReportProblem = FlowBloxGitHubRepository + "/issues/new";
        public const string FlowBloxPublicApiBaseUrl = "https://www.flowblox.net/api/";
        public const string FlowBloxInstallerManifestUrl = "https://flowblox.net/app/FlowBloxInstallerUpdates.xml";
        public const string YtDlpReleases = "https://github.com/yt-dlp/yt-dlp/releases/latest";
        public const string FFmpegDownloads = "https://ffmpeg.org/download.html";
        public const string DenoReleases = "https://github.com/denoland/deno/releases/latest";

        public const string YtDlpWindowsExecutable = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        public const string DenoWindowsExecutableZip = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
        public const string FFmpegWindowsEssentialsZip = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        public const string PythonDownloads = "https://www.python.org/downloads/";

        public static IReadOnlyDictionary<string, string> GetAll()
        {
            return typeof(GlobalUrls)
                .GetFields(BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
                .ToDictionary(
                    x => x.Name,
                    x => (string)x.GetRawConstantValue()!,
                    StringComparer.Ordinal);
        }
    }
}
