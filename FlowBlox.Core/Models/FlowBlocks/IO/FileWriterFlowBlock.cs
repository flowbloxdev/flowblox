using FlowBlox.Core.Attributes;
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

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [Display(Name = "FileWriterFlowBlock_DisplayName", Description = "FileWriterFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class FileWriterFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => FlowBlox.Core.Constants.GlobalConstants.SuccessFieldName;

        [Required]
        [Display(Name = "PropertyNames_DataSource", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association,
                     SelectionFilterMethod = nameof(GetPossibleDataSources),
                     SelectionDisplayMember = nameof(DataObjectBase.Name))]
        public DataObjectBase DataSource { get; set; }

        private IEnumerable<DataObjectBase> GetPossibleDataSources()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            return registry.GetManagedObjects<DataObjectBase>();
        }

        [ActivationCondition(MemberName = nameof(InputField), ActivationMethod = nameof(IsEncodingNameActive))]
        [ConditionallyRequired()]
        [Display(Name = "PropertyNames_EncodingName", Description = "PropertyNames_EncodingName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public DotNetEncodingNames EncodingName { get; set; }

        private bool IsEncodingNameActive()
        {
            if (this.InputField?.FieldType?.FieldType == FieldTypes.ByteArray)
                return false;

            return true;
        }

        private FieldElement _inputField;

        [Required]
        [Display(Name = "Global_InputField", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Association,
                     SelectionFilterMethod = nameof(GetPossibleFieldElements),
                     SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
                     Operations = UIOperations.Link | UIOperations.Unlink)]
        public FieldElement InputField
        {
            get => _inputField;
            set => SetRequiredInputField(ref _inputField, value);
        }

        public FileWriterFlowBlock()
        {
            this.EncodingName = DotNetEncodingNames.Default;
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_export, 16);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_export, 32);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.IO;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(DataSource));
            properties.Add(nameof(EncodingName));
            properties.Add(nameof(InputField));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return this.Invoke(runtime, data, () =>
            {

                try
                {
                    if (DataSource == null)
                        throw new InvalidOperationException("The data source is not ready for writing.");

                    if (InputField == null)
                        throw new InvalidOperationException("No input field configured.");

                    object inputValue = InputField.Value;

                    byte[] content;

                    Type configuredType = InputField.GetConfiguredType();
                    if (configuredType == typeof(byte[]))
                    {
                        content = inputValue as byte[] ?? Array.Empty<byte>();
                    }
                    else if (configuredType == typeof(string))
                    {
                        var encoding = EncodingName.ToEncoding();
                        content = encoding.GetBytes(inputValue?.ToString() ?? string.Empty);
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported input type: {configuredType}");
                    }

                    DataSource.Content = content;
                    GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
                }
                catch (Exception e)
                {
                    runtime.Report(e.ToString());
                    CreateNotification(runtime, FileWriterNotifications.FailedToWriteFile);
                    GenerateResult(runtime);
                }
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(FileWriterNotifications));
                return notificationTypes;
            }
        }
        
        public enum FileWriterNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Failed to write the file")]
            FailedToWriteFile
        }
    }
}
