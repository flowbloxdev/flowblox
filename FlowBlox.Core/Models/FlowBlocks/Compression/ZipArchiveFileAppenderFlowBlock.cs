using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Compression
{
    [Display(Name = "ZipArchiveFileAppenderFlowBlock_DisplayName", Description = "ZipArchiveFileAppenderFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class ZipArchiveFileAppenderFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Required]
        [Display(Name = "ZipArchiveFileAppenderFlowBlock_ZipArchiveObject", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Link | UIOperations.Unlink,
            SelectionFilterMethod = nameof(GetPossibleZipArchiveObjects),
            SelectionDisplayMember = nameof(Name))]
        public ZipArchiveObject ZipArchiveObject { get; set; }

        private IEnumerable<ZipArchiveObject> GetPossibleZipArchiveObjects()
        {
            return FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<ZipArchiveObject>();
        }

        [Display(Name = "ZipArchiveFileAppenderFlowBlock_ArchivePath", Description = "ZipArchiveFileAppenderFlowBlock_ArchivePath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string ArchivePath { get; set; }

        [Required]
        [Display(Name = "ZipArchiveFileAppenderFlowBlock_FileName", Description = "ZipArchiveFileAppenderFlowBlock_FileName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string FileName { get; set; }

        [Display(Name = "ZipArchiveFileAppenderFlowBlock_Password", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox]
        public string Password { get; set; }

        private FieldElement _contentField;

        [Required]
        [Display(Name = "ZipArchiveFileAppenderFlowBlock_ContentField", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleFieldElements),
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            Operations = UIOperations.Link | UIOperations.Unlink)]
        public FieldElement ContentField
        {
            get => _contentField;
            set => SetRequiredInputField(ref _contentField, value);
        }

        [ActivationCondition(MemberName = nameof(ContentField), ActivationMethod = nameof(IsEncodingNameActive))]
        [ConditionallyRequired]
        [Display(Name = "PropertyNames_EncodingName", Description = "PropertyNames_EncodingName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public DotNetEncodingNames EncodingName { get; set; }

        private bool IsEncodingNameActive()
        {
            if (ContentField?.FieldType?.FieldType == FieldTypes.ByteArray)
                return false;

            return true;
        }

        public ZipArchiveFileAppenderFlowBlock()
        {
            ArchivePath = "/";
            EncodingName = DotNetEncodingNames.Default;
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_edit, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_edit, 32, SKColors.SteelBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Compression;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(ZipArchiveObject));
            properties.Add(nameof(ArchivePath));
            properties.Add(nameof(FileName));
            properties.Add(nameof(Password));
            properties.Add(nameof(ContentField));
            return properties;
        }

        private string BuildEntryPath(string archivePath, string fileName)
        {
            var resolvedFileName = fileName?.Trim();
            if (string.IsNullOrWhiteSpace(resolvedFileName))
                throw new InvalidOperationException("No file name was provided.");

            var normalizedArchivePath = archivePath?.Trim().Replace('\\', '/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedArchivePath) || normalizedArchivePath == "/")
                return resolvedFileName;

            if (!normalizedArchivePath.StartsWith('/'))
                throw new InvalidOperationException("The archive path must be absolute and start with '/'.");

            var cleanArchivePath = normalizedArchivePath.Trim('/');
            if (string.IsNullOrWhiteSpace(cleanArchivePath))
                return resolvedFileName;

            return $"{cleanArchivePath}/{resolvedFileName}";
        }

        private byte[] ResolveContentBytes()
        {
            if (ContentField == null)
                throw new InvalidOperationException("No content field is configured.");

            var configuredType = ContentField.GetConfiguredType();
            var value = ContentField.Value;

            if (configuredType == typeof(byte[]))
                return value as byte[] ?? Array.Empty<byte>();

            if (configuredType == typeof(string))
                return EncodingName.ToEncoding().GetBytes(value?.ToString() ?? string.Empty);

            throw new NotSupportedException($"Unsupported content field type: {configuredType}");
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (ZipArchiveObject == null)
                    throw new InvalidOperationException("No ZIP archive object is assigned to the flow block.");

                try
                {
                    var entryPath = BuildEntryPath(
                        this.Resolve(owner => owner.ArchivePath),
                        this.Resolve(owner => owner.FileName));

                    var password = this.Resolve(owner => owner.Password);
                    var content = ResolveContentBytes();

                    ZipArchiveObject.AppendOrReplaceEntry(password, entryPath, content);
                    GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
                }
                catch (Exception e)
                {
                    runtime.Report(e.ToString());
                    CreateNotification(runtime, ZipArchiveFileAppenderNotifications.FailedToAppendFile);
                    GenerateResult(runtime);
                }
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(ZipArchiveFileAppenderNotifications));
                return notificationTypes;
            }
        }

        public enum ZipArchiveFileAppenderNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Failed to append file to ZIP archive")]
            FailedToAppendFile
        }
    }
}
