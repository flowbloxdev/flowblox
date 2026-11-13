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
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Compression
{
    [Display(Name = "ZipArchiveFileIteratorFlowBlock_DisplayName", Description = "ZipArchiveFileIteratorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class ZipArchiveFileIteratorFlowBlock : BaseResultFlowBlock
    {
        [Required]
        [Display(Name = "ZipArchiveFileIteratorFlowBlock_ZipArchiveObject", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleZipArchiveObjects), DisplayMember = nameof(Name))]
        public ZipArchiveObject ZipArchiveObject { get; set; }

        private IEnumerable<ZipArchiveObject> GetPossibleZipArchiveObjects()
        {
            return FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<ZipArchiveObject>();
        }

        [Display(Name = "ZipArchiveFileIteratorFlowBlock_Password", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxTextBox]
        public string Password { get; set; }

        [Display(Name = "ZipArchiveFileIteratorFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxDataGrid]
        public ObservableCollection<ResultFieldByEnumValue<ZipArchiveIteratorDestinations>> ResultFields { get; set; }

        [ActivationCondition(MemberName = nameof(ResultFields), ActivationMethod = nameof(IsEncodingNameActive))]
        [ConditionallyRequired]
        [Display(Name = "PropertyNames_EncodingName", Description = "PropertyNames_EncodingName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public DotNetEncodingNames EncodingName { get; set; }

        public ZipArchiveFileIteratorFlowBlock()
        {
            ResultFields = new ObservableCollection<ResultFieldByEnumValue<ZipArchiveIteratorDestinations>>();
            EncodingName = DotNetEncodingNames.Default;
        }

        public override void OnAfterCreate()
        {
            CreateDefaultResultFields();
            base.OnAfterCreate();
        }

        private void CreateDefaultResultFields()
        {
            CreateDestinationResultField(ResultFields, ZipArchiveIteratorDestinations.File);
            CreateDestinationResultField(ResultFields, ZipArchiveIteratorDestinations.Content);
        }

        public override List<FieldElement> Fields
        {
            get
            {
                return ResultFields
                    .Where(x => x.EnumValue != null)
                    .Select(x => x.ResultField)
                    .ExceptNull()
                    .ToList();
            }
        }

        private bool IsEncodingNameActive()
        {
            var contentResultField = ResultFields
                .FirstOrDefault(x => x?.EnumValue == ZipArchiveIteratorDestinations.Content)
                ?.ResultField;

            if (contentResultField?.FieldType?.FieldType == FieldTypes.ByteArray)
                return false;

            return true;
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_import, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_import, 32, SKColors.SteelBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Compression;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(ZipArchiveObject));
            properties.Add(nameof(Password));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var resultMap = new List<Dictionary<FieldElement, string>>();

                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                if (ZipArchiveObject == null)
                    throw new InvalidOperationException("No ZIP archive object is assigned to the flow block.");

                try
                {
                    var password = this.Resolve(owner => owner.Password);
                    var contentFieldType = ResultFields
                        .FirstOrDefault(x => x?.EnumValue == ZipArchiveIteratorDestinations.Content)
                        ?.ResultField
                        ?.GetConfiguredType();

                    var entries = ZipArchiveObject.ReadFileEntries(password);

                    foreach (var entry in entries)
                    {
                        var contentValue = string.Empty;

                        if (ResultFields.Any(x => x?.EnumValue == ZipArchiveIteratorDestinations.Content))
                        {
                            if (contentFieldType == typeof(byte[]))
                                contentValue = Convert.ToBase64String(entry.Content);
                            else
                                contentValue = EncodingName.ToEncoding().GetString(entry.Content);
                        }

                        var row = new ResultFieldByEnumValueResultBuilder<ZipArchiveIteratorDestinations>()
                            .For(ZipArchiveIteratorDestinations.File, entry.File)
                            .For(ZipArchiveIteratorDestinations.Content, contentValue)
                            .Build(ResultFields);

                        resultMap.Add(row);
                    }

                    if (!resultMap.Any())
                        CreateNotification(runtime, ZipArchiveFileIteratorNotifications.NoFilesFound);

                    GenerateResult(runtime, resultMap);
                }
                catch (Exception e)
                {
                    runtime.Report(e.ToString());
                    CreateNotification(runtime, ZipArchiveFileIteratorNotifications.FailedToReadArchive);
                    GenerateResult(runtime);
                }
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(ZipArchiveFileIteratorNotifications));
                return notificationTypes;
            }
        }

        public enum ZipArchiveFileIteratorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Failed to read ZIP archive")]
            FailedToReadArchive,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "No files found in ZIP archive")]
            NoFilesFound
        }
    }
}
