using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Downloads;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    [Display(Name = "YtDlpMediaDownloadProvider_DisplayName", Description = "YtDlpMediaDownloadProvider_Description",
        ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("YtDlpMediaDownloadProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class YtDlpMediaDownloadProvider : FFmpegMediaDownloadProviderBase
    {
        [Required]
        [Display(Name = "YtDlpMediaDownloadProvider_ExecutablePath", Description = "YtDlpMediaDownloadProvider_ExecutablePath_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFileSelection)]
        public string ExecutablePath { get; set; } = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

        [Display(Name = "YtDlpMediaDownloadProvider_DenoPath", Description = "YtDlpMediaDownloadProvider_DenoPath_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFileSelection)]
        public string DenoPath { get; set; } = string.Empty;

        protected override async Task<string> DownloadCoreAsync(
            MediaDownloadRequest request,
            TransferProgressReporter progress,
            CancellationToken cancellationToken)
        {
            var outputDirectory = Path.GetFullPath(request.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            var downloader = new YoutubeDL
            {
                YoutubeDLPath = this.Resolve(provider => provider.ExecutablePath, required: true),
                FFmpegPath = ResolveFFmpegExecutablePath(),
                OutputFolder = outputDirectory,
                OutputFileTemplate = "%(title)s [%(id)s].%(ext)s",
                OverwriteFiles = request.OverwriteFiles,
                IgnoreDownloadErrors = false
            };

            var options = new OptionSet
            {
                NoPlaylist = true,
                PlaylistItems = "1",
                NoPart = false
            };
            var denoPath = this.Resolve(provider => provider.DenoPath);
            if (!string.IsNullOrWhiteSpace(denoPath))
                options.AddCustomOption("--js-runtimes", "deno:" + denoPath);

            if (request.AudioBitrateKbps.HasValue &&
                request.Format != MediaDownloadFormat.Video &&
                request.Format != MediaDownloadFormat.Audio)
            {
                options.AddCustomOption("--audio-quality",
                    request.AudioBitrateKbps.Value.ToString(CultureInfo.InvariantCulture) + "K");
            }

            var adapter = new YtDlpProgressAdapter(progress);
            progress.Report("Preparing media download");
            RunResult<string> result;
            if (request.Format == MediaDownloadFormat.Video || request.Format == MediaDownloadFormat.Audio)
            {
                result = await downloader.RunVideoDownload(
                    request.Url,
                    format: request.Format == MediaDownloadFormat.Audio ? "bestaudio" : "bestvideo+bestaudio/best",
                    ct: cancellationToken,
                    progress: adapter,
                    overrideOptions: options).ConfigureAwait(false);
            }
            else
            {
                var audioFormat = request.Format switch
                {
                    MediaDownloadFormat.Mp3 => AudioConversionFormat.Mp3,
                    MediaDownloadFormat.M4a => AudioConversionFormat.M4a,
                    MediaDownloadFormat.Opus => AudioConversionFormat.Opus,
                    _ => throw new ValidationException("The media output format is unsupported.")
                };
                result = await downloader.RunAudioDownload(
                    request.Url,
                    audioFormat,
                    ct: cancellationToken,
                    progress: adapter,
                    overrideOptions: options).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Success)
                throw new IOException("yt-dlp media download failed: " + string.Join(Environment.NewLine, result.ErrorOutput));

            return result.Data;
        }
    }
}
