using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.OpenXml;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [FlowBloxUIGroup("OpenXml2TextFlowBlock_Groups_Advanced", 0)]
    [Display(Name = "OpenXml2TextFlowBlock_DisplayName", Description = "OpenXml2TextFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class OpenXml2TextFlowBlock : BaseSingleResultFlowBlock
    {
        private FieldElement _wordDocumentField;

        [Required]
        [Display(Name = "OpenXml2TextFlowBlock_WordDocumentField", Description = "OpenXml2TextFlowBlock_WordDocumentField_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleFieldElements), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement WordDocumentField
        {
            get => _wordDocumentField;
            set => SetRequiredInputField(ref _wordDocumentField, value);
        }

        [Display(Name = "OpenXml2TextFlowBlock_HeaderMode", Description = "OpenXml2TextFlowBlock_HeaderMode_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenXml2TextFlowBlock_Groups_Advanced", Order = 0)]
        public OpenXmlTextSectionMode HeaderMode { get; set; } = OpenXmlTextSectionMode.IncludeWithTags;

        [Display(Name = "OpenXml2TextFlowBlock_BodyMode", Description = "OpenXml2TextFlowBlock_BodyMode_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenXml2TextFlowBlock_Groups_Advanced", Order = 1)]
        public OpenXmlTextSectionMode BodyMode { get; set; } = OpenXmlTextSectionMode.IncludeWithTags;

        [Display(Name = "OpenXml2TextFlowBlock_FooterMode", Description = "OpenXml2TextFlowBlock_FooterMode_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenXml2TextFlowBlock_Groups_Advanced", Order = 2)]
        public OpenXmlTextSectionMode FooterMode { get; set; } = OpenXmlTextSectionMode.IncludeWithTags;

        [Display(Name = "OpenXml2TextFlowBlock_SplitAtPageBreaks", Description = "OpenXml2TextFlowBlock_SplitAtPageBreaks_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenXml2TextFlowBlock_Groups_Advanced", Order = 3)]
        public bool SplitAtPageBreaks { get; set; } = true;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_arrow_right_outline, 16, SKColors.RoyalBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_arrow_right_outline, 32, SKColors.RoyalBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Conversion;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(OpenXml2TextNotifications));
                return notificationTypes;
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(WordDocumentField));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {
                try
                {
                    var documentBytes = FlowBloxBinaryContentHelper.Resolve(WordDocumentField);
                    var pages = OpenXmlTextExtractor.Extract(documentBytes, new OpenXmlTextExtractionOptions
                    {
                        HeaderMode = this.Resolve(x => x.HeaderMode),
                        BodyMode = this.Resolve(x => x.BodyMode),
                        FooterMode = this.Resolve(x => x.FooterMode),
                        SplitAtPageBreaks = this.Resolve(x => x.SplitAtPageBreaks)
                    });
                    GenerateResult(runtime, pages);
                }
                catch (Exception ex)
                {
                    runtime.Report("Failed to extract text from the Open XML Word document: " + ex);
                    CreateNotification(runtime, OpenXml2TextNotifications.FailedToExtractText);
                    GenerateResult(runtime);
                }
            });
        }

        public enum OpenXml2TextNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "OpenXml2TextFlowBlock_Notification_FailedToExtractText", ResourceType = typeof(FlowBloxTexts))]
            FailedToExtractText
        }
    }
}
