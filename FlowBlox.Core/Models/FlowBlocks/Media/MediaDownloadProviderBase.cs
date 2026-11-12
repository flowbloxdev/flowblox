using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Downloads;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    [Display(Name = "MediaDownloadProviderBase_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("MediaDownloadProviderBase_DisplayName_Plural", typeof(FlowBloxTexts))]
    public abstract class MediaDownloadProviderBase : ManagedObject
    {
        [Range(1, 86400)]
        [Display(Name = "MediaDownloadProviderBase_TimeoutSeconds", Description = "MediaDownloadProviderBase_TimeoutSeconds_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int TimeoutSeconds { get; set; } = 900;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.download, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.download, 32, SKColors.SteelBlue);

        public abstract bool SupportsFormat(MediaDownloadFormat format);

        internal string GetUnsupportedFormatMessage(MediaDownloadFormat format) => string.Format(
            FlowBloxResourceUtil.GetLocalizedString("MediaDownloadProviderBase_UnsupportedFormat", typeof(FlowBloxTexts)),
            Enum.IsDefined(format) ? format.GetDisplayName() : format.ToString());

        public async Task<IReadOnlyList<MediaDownloadResult>> DownloadAsync(
            MediaDownloadRequest request,
            TransferProgressReporter progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(progress);
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ValidationException("A valid HTTP or HTTPS media URL is required.");

            if (string.IsNullOrWhiteSpace(request.OutputDirectory))
                throw new ValidationException("A download output directory is required.");
            if (!Enum.IsDefined(request.Format))
                throw new ValidationException("The media output format is invalid.");
            if (!SupportsFormat(request.Format))
                throw new ValidationException(GetUnsupportedFormatMessage(request.Format));

            if (request.AudioBitrateKbps.HasValue &&
                (request.AudioBitrateKbps.Value < 32 || request.AudioBitrateKbps.Value > 320))
                throw new ValidationException("The audio bitrate must be between 32 and 320 kbps.");

            var timeoutSeconds = this.Resolve(provider => provider.TimeoutSeconds);
            if (timeoutSeconds < 1 || timeoutSeconds > 86400)
                throw new ValidationException("The media download timeout must be between 1 and 86400 seconds.");

            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                var results = await DownloadCoreAsync(request, progress, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                var validatedResults = results
                    .Select(result => ValidateResult(result, request.Url))
                    .ToArray();

                if (validatedResults.Length > 0 && validatedResults.All(result => result.Success))
                    progress.Complete();

                return validatedResults;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new TimeoutException("The media download exceeded its configured timeout.");
            }
        }

        private static MediaDownloadResult ValidateResult(MediaDownloadResult result, string sourceUrl)
        {
            var success = result.Success && !string.IsNullOrWhiteSpace(result.FilePath) && File.Exists(result.FilePath);
            var failureReason = result.FailureReason;
            var errorMessage = result.ErrorMessage;
            if (success)
            {
                failureReason = MediaDownloadFailureReason.None;
                errorMessage = string.Empty;
            }
            else
            {
                if (result.Success)
                    failureReason = MediaDownloadFailureReason.OutputFileMissing;
                else if (failureReason == MediaDownloadFailureReason.None)
                    failureReason = MediaDownloadFailureReason.DownloadFailed;

                if (string.IsNullOrWhiteSpace(errorMessage))
                    errorMessage = "The media provider did not produce an output file.";
            }

            return new MediaDownloadResult
            {
                Success = success,
                FilePath = success ? Path.GetFullPath(result.FilePath) : string.Empty,
                SourceUrl = string.IsNullOrWhiteSpace(result.SourceUrl) ? sourceUrl : result.SourceUrl,
                FailureReason = failureReason,
                ErrorMessage = errorMessage
            };
        }

        protected abstract Task<IReadOnlyList<MediaDownloadResult>> DownloadCoreAsync(
            MediaDownloadRequest request,
            TransferProgressReporter progress,
            CancellationToken cancellationToken);
    }
}
