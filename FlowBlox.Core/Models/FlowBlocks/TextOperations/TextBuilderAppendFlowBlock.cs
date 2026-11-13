using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "TextBuilderAppendFlowBlock_DisplayName", Description = "TextBuilderAppendFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("TextBuilderAppendFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    public class TextBuilderAppendFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Display(Name = "TextBuilderAppendFlowBlock_AssociatedTextBuilder", Description = "TextBuilderAppendFlowBlock_AssociatedTextBuilder_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [AssociatedFlowBlockResolvable]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleTextBuilderFlowBlocks), DisplayMember = nameof(Name))]
        public TextBuilderFlowBlock AssociatedTextBuilder { get; set; }

        [Required]
        [Display(Name = "TextBuilderAppendFlowBlock_Text", Description = "TextBuilderAppendFlowBlock_Text_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxTextBox(MultiLine = true)]
        public string Text { get; set; }

        [Display(Name = "TextBuilderAppendFlowBlock_AppendLine", Description = "TextBuilderAppendFlowBlock_AppendLine_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public bool AppendLine { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_box_plus_outline, 16, SKColors.SlateBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_box_plus_outline, 32, SKColors.SlateBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        private List<TextBuilderFlowBlock> GetPossibleTextBuilderFlowBlocks()
            => FlowBloxRegistryProvider.GetRegistry().GetFlowBlocks<TextBuilderFlowBlock>().ToList();

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(AssociatedTextBuilder));
            properties.Add(nameof(Text));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var textBuilder = AssociatedTextBuilder ?? GetPreviousFlowBlockOnPath<TextBuilderFlowBlock>(this);
                if (textBuilder?.InternalStringBuilder == null)
                    throw new InvalidOperationException("No initialized text builder source is assigned.");

                var text = this.Resolve(owner => owner.Text);
                if (this.Resolve(x => x.AppendLine))
                    textBuilder.InternalStringBuilder.AppendLine(text);
                else
                    textBuilder.InternalStringBuilder.Append(text);

                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }
    }
}
