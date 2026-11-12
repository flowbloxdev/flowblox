using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Downloads;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    [Display(Name = "MediaDownloadFlowBlock_DisplayName", Description = "MediaDownloadFlowBlock_Description",
        ResourceType = typeof(FlowBloxTexts))]
    public sealed class MediaDownloadFlowBlock : BaseResultFlowBlock
    {
        [Required]
        [Display(Name = "MediaDownloadFlowBlock_Provider", Description = "MediaDownloadFlowBlock_Provider_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association, SelectionFilterMethod = nameof(GetPossibleProviders),
            SelectionDisplayMember = nameof(MediaDownloadProviderBase.Name))]
        public MediaDownloadProviderBase? Provider { get; set; }

        [Required]
        [Display(Name = "MediaDownloadFlowBlock_Url", Description = "MediaDownloadFlowBlock_Url_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Url { get; set; } = string.Empty;

        [Required]
        [Display(Name = "MediaDownloadFlowBlock_OutputDirectory", Description = "MediaDownloadFlowBlock_OutputDirectory_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFolderSelection)]
        public string OutputDirectory { get; set; } = string.Empty;

        [Display(Name = "MediaDownloadFlowBlock_Format", Description = "MediaDownloadFlowBlock_Format_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        [DependsOnProperty(MemberName = nameof(Provider))]
        [CustomValidation(typeof(MediaDownloadFlowBlock), nameof(ValidateFormat))]
        public MediaDownloadFormat Format { get; set; } = MediaDownloadFormat.Video;

        [Range(32, 320)]
        [Display(Name = "MediaDownloadFlowBlock_AudioBitrateKbps", Description = "MediaDownloadFlowBlock_AudioBitrateKbps_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [ActivationCondition(ActivationMethod = nameof(IsAudioBitrateActive))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int? AudioBitrateKbps { get; set; }

        [Display(Name = "MediaDownloadFlowBlock_OverwriteFiles", Description = "MediaDownloadFlowBlock_OverwriteFiles_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool OverwriteFiles { get; set; }

        [Display(Name = "MediaDownloadFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<ResultFieldByEnumValue<MediaDownloadDestinations>> ResultFields { get; set; } = [];

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.download, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.download, 32, SKColors.SteelBlue);
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Web;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<FieldElement> Fields => ResultFields
            .Where(resultField => resultField.EnumValue != null)
            .Select(resultField => resultField.ResultField)
            .ExceptNull()
            .ToList();

        public IEnumerable<MediaDownloadProviderBase> GetPossibleProviders() =>
            FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<MediaDownloadProviderBase>();

        public static ValidationResult? ValidateFormat(MediaDownloadFormat format, ValidationContext validationContext)
        {
            var block = (MediaDownloadFlowBlock)validationContext.ObjectInstance;
            var provider = block.Provider;
            if (provider == null)
                return ValidationResult.Success;

            if (!string.IsNullOrWhiteSpace(block.GetPropertySelectedFieldName(nameof(Format))))
                format = block.Resolve(owner => owner.Format);
            return provider.SupportsFormat(format)
                ? ValidationResult.Success
                : new ValidationResult(provider.GetUnsupportedFormatMessage(format), [nameof(Format)]);
        }

        private bool IsAudioBitrateActive()
        {
            var format = this.Resolve(block => block.Format);
            return format == MediaDownloadFormat.Mp3 ||
                format == MediaDownloadFormat.M4a ||
                format == MediaDownloadFormat.Opus;
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Provider));
            properties.Add(nameof(Url));
            properties.Add(nameof(OutputDirectory));
            properties.Add(nameof(Format));
            return properties;
        }

        public override void OnAfterCreate()
        {
            if (!ResultFields.Any())
            {
                CreateDestinationResultField(ResultFields, MediaDownloadDestinations.Success, FieldTypes.Boolean);
                CreateDestinationResultField(ResultFields, MediaDownloadDestinations.FilePath);
            }

            base.OnAfterCreate();
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {
                var url = this.Resolve(block => block.Url).Trim();
                IReadOnlyList<MediaDownloadResult> results;
                try
                {
                    if (Provider == null)
                        throw new ValidationException("No media download provider has been configured.");

                    var request = new MediaDownloadRequest
                    {
                        Url = url,
                        OutputDirectory = this.Resolve(block => block.OutputDirectory),
                        Format = this.Resolve(block => block.Format),
                        AudioBitrateKbps = IsAudioBitrateActive() ? this.Resolve(block => block.AudioBitrateKbps) : null,
                        OverwriteFiles = this.Resolve(block => block.OverwriteFiles)
                    };
                    var progress = new TransferProgressReporter(message => runtime.Report(message, FlowBloxLogLevel.Info));
                    results = Provider.DownloadAsync(request, progress, runtime.GetCancellationToken()).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw new RuntimeCancellationException();
                }
                catch (Exception exception)
                {
                    runtime.Report("Media download failed.", FlowBloxLogLevel.Warning, exception);
                    results =
                    [
                        new MediaDownloadResult
                        {
                            SourceUrl = url,
                            FailureReason = exception is TimeoutException
                                ? MediaDownloadFailureReason.Timeout
                                : MediaDownloadFailureReason.DownloadFailed,
                            ErrorMessage = exception.Message
                        }
                    ];
                }

                foreach (var result in results.Where(result => !result.Success))
                    runtime.Report("Media download entry failed: " + result.SourceUrl + ". " + result.ErrorMessage, FlowBloxLogLevel.Warning);

                foreach (var reason in results.Where(result => !result.Success).Select(result => result.FailureReason).Distinct())
                    CreateNotification(runtime, GetFailureNotification(reason));

                if (results.Count == 0)
                    CreateNotification(runtime, MediaDownloadNotifications.NoMediaFound);

                var rows = results.Select(CreateResultRow).ToArray();
                GenerateResult(runtime, rows);
            });
        }

        private Dictionary<FieldElement, string> CreateResultRow(MediaDownloadResult result)
        {
            var filePath = result.Success ? result.FilePath : string.Empty;
            var fileSize = result.Success
                ? new FileInfo(filePath).Length.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            return new ResultFieldByEnumValueResultBuilder<MediaDownloadDestinations>()
                .For(MediaDownloadDestinations.Success, result.Success.ToString(CultureInfo.InvariantCulture))
                .For(MediaDownloadDestinations.FilePath, filePath)
                .For(MediaDownloadDestinations.FileName, Path.GetFileName(filePath))
                .For(MediaDownloadDestinations.FileSize, fileSize)
                .For(MediaDownloadDestinations.SourceUrl, result.SourceUrl)
                .For(MediaDownloadDestinations.ErrorMessage, result.ErrorMessage)
                .For(MediaDownloadDestinations.FailureReason, result.FailureReason.ToString())
                .Build(ResultFields);
        }

        private static MediaDownloadNotifications GetFailureNotification(MediaDownloadFailureReason reason) => reason switch
        {
            MediaDownloadFailureReason.PlaylistNotSupported => MediaDownloadNotifications.PlaylistNotSupported,
            MediaDownloadFailureReason.OutputFileMissing => MediaDownloadNotifications.OutputFileMissing,
            MediaDownloadFailureReason.Timeout => MediaDownloadNotifications.Timeout,
            _ => MediaDownloadNotifications.DownloadFailed
        };

        public override List<Type> NotificationTypes
        {
            get
            {
                var types = base.NotificationTypes;
                types.Add(typeof(MediaDownloadNotifications));
                return types;
            }
        }

        public enum MediaDownloadNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "MediaDownloadNotifications_DownloadFailed", ResourceType = typeof(FlowBloxTexts))]
            DownloadFailed,
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "MediaDownloadNotifications_NoMediaFound", ResourceType = typeof(FlowBloxTexts))]
            NoMediaFound,
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "MediaDownloadNotifications_PlaylistNotSupported", ResourceType = typeof(FlowBloxTexts))]
            PlaylistNotSupported,
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "MediaDownloadNotifications_OutputFileMissing", ResourceType = typeof(FlowBloxTexts))]
            OutputFileMissing,
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "MediaDownloadNotifications_Timeout", ResourceType = typeof(FlowBloxTexts))]
            Timeout
        }
    }
}
