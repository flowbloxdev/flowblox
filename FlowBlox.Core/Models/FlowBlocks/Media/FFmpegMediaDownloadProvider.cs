using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using FFMpegCore;
using FFMpegCore.Enums;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Downloads;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    [Display(Name = "FFmpegMediaDownloadProvider_DisplayName", Description = "FFmpegMediaDownloadProvider_Description",
        ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("FFmpegMediaDownloadProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class FFmpegMediaDownloadProvider : FFmpegMediaDownloadProviderBase
    {
        [Display(Name = "FFmpegMediaDownloadProvider_OutputName", Description = "FFmpegMediaDownloadProvider_OutputName_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string OutputName { get; set; } = string.Empty;

        [Range(1, 86400)]
        [Display(Name = "FFmpegMediaDownloadProvider_RecordingDurationSeconds",
            Description = "FFmpegMediaDownloadProvider_RecordingDurationSeconds_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int? RecordingDurationSeconds { get; set; }

        public override bool SupportsFormat(MediaDownloadFormat format) => format switch
        {
            MediaDownloadFormat.Video or
            MediaDownloadFormat.Audio or
            MediaDownloadFormat.Mp3 or
            MediaDownloadFormat.M4a or
            MediaDownloadFormat.Opus => true,
            _ => false
        };

        protected override async Task<IReadOnlyList<MediaDownloadResult>> DownloadCoreAsync(
            MediaDownloadRequest request,
            TransferProgressReporter progress,
            CancellationToken cancellationToken)
        {
            var outputName = this.Resolve(provider => provider.OutputName).Trim();
            var durationSeconds = this.Resolve(provider => provider.RecordingDurationSeconds);
            if (durationSeconds.HasValue && (durationSeconds < 1 || durationSeconds > 86400))
                throw new ValidationException("The recording duration must be between 1 and 86400 seconds.");

            var outputDirectory = Path.GetFullPath(request.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = CreateOutputPath(request, outputDirectory, outputName);
            if (!request.OverwriteFiles && File.Exists(outputPath))
            {
                progress.Report("Using existing media file");
                return CreateResult(outputPath, request.Url);
            }

            var temporaryPath = Path.Combine(outputDirectory,
                ".flowblox-" + Guid.NewGuid().ToString("N") + Path.GetExtension(outputPath));
            try
            {
                progress.Report("Downloading and processing media");
                var processor = FFMpegArguments
                    .FromUrlInput(new Uri(request.Url))
                    .OutputToFile(
                        temporaryPath,
                        overwrite: false,
                        options => ConfigureOutput(options, request, durationSeconds))
                    .NotifyOnProgress((TimeSpan position) => progress.ReportPosition(position))
                    .CancellableThrough(cancellationToken);

                await processor.ProcessAsynchronously(
                    throwOnError: true,
                    ffMpegOptions: ResolveFFmpegOptions()).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, outputPath, request.OverwriteFiles);
                return CreateResult(outputPath, request.Url);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static IReadOnlyList<MediaDownloadResult> CreateResult(string filePath, string sourceUrl) =>
        [
            new MediaDownloadResult
            {
                Success = true,
                FilePath = filePath,
                SourceUrl = sourceUrl
            }
        ];

        internal static string CreateOutputPath(MediaDownloadRequest request, string outputDirectory, string outputName)
        {
            if (string.IsNullOrWhiteSpace(outputName))
                outputName = "media-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Url)))[..16];

            if (outputName == "." || outputName == ".." ||
                outputName != Path.GetFileName(outputName) ||
                outputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ValidationException("The media output name must be a file name without a directory.");

            var extension = request.Format switch
            {
                MediaDownloadFormat.Video => ".mkv",
                MediaDownloadFormat.Audio => ".mka",
                MediaDownloadFormat.Mp3 => ".mp3",
                MediaDownloadFormat.M4a => ".m4a",
                MediaDownloadFormat.Opus => ".opus",
                _ => throw new ValidationException("The media output format is unsupported.")
            };
            return Path.Combine(outputDirectory, outputName + extension);
        }

        private static void ConfigureOutput(
            FFMpegArgumentOptions options,
            MediaDownloadRequest request,
            int? durationSeconds)
        {
            if (durationSeconds.HasValue)
                options.WithDuration(TimeSpan.FromSeconds(durationSeconds.Value));

            if (request.Format == MediaDownloadFormat.Video)
            {
                options.WithCustomArgument("-map 0:v? -map 0:a?").CopyChannel();
                return;
            }

            options.WithCustomArgument("-map 0:a:0").DisableChannel(Channel.Video);
            var codec = request.Format switch
            {
                MediaDownloadFormat.Audio => "copy",
                MediaDownloadFormat.Mp3 => "libmp3lame",
                MediaDownloadFormat.M4a => "aac",
                MediaDownloadFormat.Opus => "libopus",
                _ => throw new ValidationException("The media output format is unsupported.")
            };
            options.WithAudioCodec(codec);
            if (request.AudioBitrateKbps.HasValue && request.Format != MediaDownloadFormat.Audio)
                options.WithAudioBitrate(request.AudioBitrateKbps.Value);
        }
    }
}
