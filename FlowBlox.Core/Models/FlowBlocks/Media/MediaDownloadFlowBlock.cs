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
                string filePath = string.Empty;
                string fileName = string.Empty;
                string fileSize = string.Empty;
                var success = false;
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
                    filePath = Provider.DownloadAsync(request, progress, runtime.GetCancellationToken()).GetAwaiter().GetResult();
                    fileName = Path.GetFileName(filePath);
                    fileSize = new FileInfo(filePath).Length.ToString(CultureInfo.InvariantCulture);
                    success = true;
                }
                catch (OperationCanceledException)
                {
                    throw new RuntimeCancellationException();
                }
                catch (Exception exception)
                {
                    runtime.Report("Media download failed.", FlowBloxLogLevel.Error, exception);
                    CreateNotification(runtime, MediaDownloadNotifications.DownloadFailed);
                }

                var row = new ResultFieldByEnumValueResultBuilder<MediaDownloadDestinations>()
                    .For(MediaDownloadDestinations.Success, success.ToString(CultureInfo.InvariantCulture))
                    .For(MediaDownloadDestinations.FilePath, filePath)
                    .For(MediaDownloadDestinations.FileName, fileName)
                    .For(MediaDownloadDestinations.FileSize, fileSize)
                    .For(MediaDownloadDestinations.SourceUrl, url)
                    .Build(ResultFields);
                GenerateResult(runtime, [row]);
            });
        }

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
            DownloadFailed
        }
    }
}
