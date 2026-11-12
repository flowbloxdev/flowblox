using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Downloads;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
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

        [ActivationCondition(ActivationMethod = nameof(IsStandaloneFFmpegPathActive))]
        [ConditionallyRequired]
        [Display(Name = "YtDlpMediaDownloadProvider_FFmpegPath", Description = "YtDlpMediaDownloadProvider_FFmpegPath_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFileSelection)]
        public string FFmpegPath { get; set; } = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        [Display(Name = "YtDlpMediaDownloadProvider_DenoPath", Description = "YtDlpMediaDownloadProvider_DenoPath_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFileSelection)]
        public string DenoPath { get; set; } = string.Empty;

        [Display(Name = "YtDlpMediaDownloadProvider_DownloadPlaylist", Description = "YtDlpMediaDownloadProvider_DownloadPlaylist_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool DownloadPlaylist { get; set; }

        public override bool SupportsFormat(MediaDownloadFormat format) => format switch
        {
            MediaDownloadFormat.Video or
            MediaDownloadFormat.Audio or
            MediaDownloadFormat.Mp3 or
            MediaDownloadFormat.M4a or
            MediaDownloadFormat.Opus => true,
            _ => false
        };

        private bool IsStandaloneFFmpegPathActive() => FFmpeg == null;

        protected override string ResolveFallbackFFmpegPath() =>
            this.Resolve(provider => provider.FFmpegPath, required: true);

        protected override async Task<IReadOnlyList<MediaDownloadResult>> DownloadCoreAsync(
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

            var downloadPlaylist = this.Resolve(provider => provider.DownloadPlaylist);
            var options = new OptionSet
            {
                // YoutubeDLSharp reads stdout as UTF-8; yt-dlp must use the same encoding for file paths.
                Encoding = "utf-8",
                NoPlaylist = true,
                PlaylistItems = "1",
                NoPart = false
            };
            var denoPath = this.Resolve(provider => provider.DenoPath);
            if (!string.IsNullOrWhiteSpace(denoPath))
                options.AddCustomOption("--js-runtimes", "deno:" + denoPath);

            var adapter = new YtDlpProgressAdapter(progress);
            progress.Report("Preparing media download");
            var discoveryOptions = new OptionSet
            {
                Encoding = "utf-8",
                NoPlaylist = false,
                PlaylistItems = downloadPlaylist ? null : "1"
            };
            discoveryOptions.AddCustomOption("--yes-playlist", true);
            if (!string.IsNullOrWhiteSpace(denoPath))
                discoveryOptions.AddCustomOption("--js-runtimes", "deno:" + denoPath);

            var metadata = await downloader.RunVideoDataFetch(
                request.Url,
                ct: cancellationToken,
                flat: true,
                overrideOptions: discoveryOptions).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!metadata.Success || metadata.Data == null)
                throw new IOException("yt-dlp media discovery failed: " + string.Join(Environment.NewLine, metadata.ErrorOutput));

            return await DownloadDiscoveredMediaAsync(
                metadata.Data,
                downloadPlaylist,
                request.Url,
                (url, token) => DownloadSingleAsync(downloader, request, url, options, adapter, token),
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> DownloadSingleAsync(
            YoutubeDL downloader,
            MediaDownloadRequest request,
            string url,
            OptionSet options,
            YtDlpProgressAdapter adapter,
            CancellationToken cancellationToken)
        {
            options = (OptionSet)options.Clone();
            if (request.AudioBitrateKbps.HasValue &&
                request.Format != MediaDownloadFormat.Video &&
                request.Format != MediaDownloadFormat.Audio)
            {
                options.AddCustomOption("--audio-quality",
                    request.AudioBitrateKbps.Value.ToString(CultureInfo.InvariantCulture) + "K");
            }

            RunResult<string> result;
            if (request.Format == MediaDownloadFormat.Video || request.Format == MediaDownloadFormat.Audio)
            {
                result = await downloader.RunVideoDownload(
                    url,
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
                    url,
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

        internal static Task<IReadOnlyList<MediaDownloadResult>> DownloadDiscoveredMediaAsync(
            VideoData metadata,
            bool downloadPlaylist,
            string sourceUrl,
            Func<string, CancellationToken, Task<string>> download,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (metadata.Entries != null && !downloadPlaylist)
            {
                return Task.FromResult<IReadOnlyList<MediaDownloadResult>>(
                [
                    new MediaDownloadResult
                    {
                        SourceUrl = sourceUrl,
                        FailureReason = MediaDownloadFailureReason.PlaylistNotSupported,
                        ErrorMessage = "yt-dlp identified a playlist, but playlist downloads are disabled on the provider."
                    }
                ]);
            }

            var entries = metadata.Entries ?? [new VideoData { WebpageUrl = sourceUrl }];
            return DownloadPlaylistEntriesAsync(entries, sourceUrl, download, cancellationToken);
        }

        private static async Task<IReadOnlyList<MediaDownloadResult>> DownloadPlaylistEntriesAsync(
            IEnumerable<VideoData?> entries,
            string playlistUrl,
            Func<string, CancellationToken, Task<string>> download,
            CancellationToken cancellationToken)
        {
            var results = new List<MediaDownloadResult>();
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry?.Entries != null)
                {
                    results.AddRange(await DownloadPlaylistEntriesAsync(
                        entry.Entries,
                        playlistUrl,
                        download,
                        cancellationToken).ConfigureAwait(false));
                    continue;
                }

                var sourceUrl = entry?.WebpageUrl;
                // Generic HTML playlists can give every entry the playlist page as its webpage URL.
                // In that case use the entry's own media URL rather than downloading the first item repeatedly.
                if (string.IsNullOrWhiteSpace(sourceUrl) ||
                    (string.Equals(sourceUrl, playlistUrl, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(entry?.Url)))
                    sourceUrl = entry?.Url;

                try
                {
                    if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        throw new IOException("yt-dlp returned a playlist entry without a valid media URL.");

                    var filePath = await download(sourceUrl, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(new MediaDownloadResult
                    {
                        Success = true,
                        SourceUrl = sourceUrl,
                        FilePath = filePath
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    results.Add(new MediaDownloadResult
                    {
                        SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? playlistUrl : sourceUrl,
                        FailureReason = MediaDownloadFailureReason.DownloadFailed,
                        ErrorMessage = exception.Message
                    });
                }
            }

            return results;
        }
    }
}
