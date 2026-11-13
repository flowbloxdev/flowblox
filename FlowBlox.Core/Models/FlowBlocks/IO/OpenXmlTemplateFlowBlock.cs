using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.OpenXml;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [Display(Name = "OpenXmlTemplateFlowBlock_DisplayName", Description = "OpenXmlTemplateFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class OpenXmlTemplateFlowBlock : BaseSingleResultFlowBlock
    {
        private FieldElement _templateField;

        public override FieldTypes DefaultResultFieldType => FieldTypes.ByteArray;

        [Required]
        [Display(Name = "OpenXmlTemplateFlowBlock_TemplateField", Description = "OpenXmlTemplateFlowBlock_TemplateField_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleFieldElements), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement TemplateField
        {
            get => _templateField;
            set => SetRequiredInputField(ref _templateField, value);
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_edit_outline, 16, SKColors.RoyalBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_edit_outline, 32, SKColors.RoyalBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Generation;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(TemplateField));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var templateBytes = FlowBloxBinaryContentHelper.Resolve(TemplateField);
                var documentBytes = OpenXmlTemplateProcessor.ReplacePlaceholders(
                    templateBytes,
                    FlowBloxFieldHelper.ReplaceFieldsInString);

                GenerateResult(runtime, Convert.ToBase64String(documentBytes));
            });
        }
    }
}
