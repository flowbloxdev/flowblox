using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "TextBuilderOutputFlowBlock_DisplayName", Description = "TextBuilderOutputFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("TextBuilderOutputFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    public class TextBuilderOutputFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "TextBuilderOutputFlowBlock_AssociatedTextBuilder", Description = "TextBuilderOutputFlowBlock_AssociatedTextBuilder_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [AssociatedFlowBlockResolvable]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleTextBuilderFlowBlocks), DisplayMember = nameof(Name))]
        public TextBuilderFlowBlock AssociatedTextBuilder { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_box_check_outline, 16, SKColors.MediumSlateBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_box_check_outline, 32, SKColors.MediumSlateBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        private List<TextBuilderFlowBlock> GetPossibleTextBuilderFlowBlocks()
            => FlowBloxRegistryProvider.GetRegistry().GetFlowBlocks<TextBuilderFlowBlock>().ToList();

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(AssociatedTextBuilder));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var textBuilder = AssociatedTextBuilder ?? GetPreviousFlowBlockOnPath<TextBuilderFlowBlock>(this);
                if (textBuilder?.InternalStringBuilder == null)
                    throw new InvalidOperationException("No initialized text builder source is assigned.");

                GenerateResult(runtime, textBuilder.InternalStringBuilder.ToString());
            });
        }
    }
}
