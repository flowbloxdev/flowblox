using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Runtime;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Util.Resources;
using System.Collections.ObjectModel;
using SkiaSharp;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.Core.Models.FlowBlocks.Logic
{
    [FlowBloxUIGroup("DecisionFlowBlock_Groups_Decisions", 0)]
    [Display(Name = "DecisionFlowBlock_DisplayName", Description = "DecisionFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("DecisionFlowBlock_SpecialExplanation_ConditionalOutputs", Icon = SpecialExplanationIcon.Hint)]
    public class DecisionFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "DecisionFlowBlock_Decisions", Description = "DecisionFlowBlock_Decisions_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "DecisionFlowBlock_Groups_Decisions", Order = 0)]
        [FlowBloxDataGrid(
            ColumnMemberNames = new[]
            {
                nameof(FieldComparisonCondition.FieldElement),
                nameof(FieldComparisonCondition.Operator),
                nameof(FieldComparisonCondition.Value),
                nameof(FieldComparisonCondition.OutputValue)
            },
            IsMovable = true)]
        public ObservableCollection<FieldComparisonCondition> Decisions { get; set; }

        [Display(Name = "DecisionFlowBlock_FallbackValue", Description = "DecisionFlowBlock_FallbackValue_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "DecisionFlowBlock_Groups_Decisions", Order = 2)]
        [FlowBloxTextBox(MultiLine = true)]
        public string FallbackValue { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.source_branch, 16, SKColors.Goldenrod);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.source_branch, 32, SKColors.Goldenrod);

        public DecisionFlowBlock()
        {
            Decisions = new ObservableCollection<FieldComparisonCondition>();
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Logic;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                foreach (var decision in Decisions)
                {
                    if (decision.Compare())
                    {
                        var result = string.IsNullOrEmpty(decision.OutputValue)
                            ? decision.FieldElement?.StringValue
                            : decision.Resolve(owner => owner.OutputValue);

                        GenerateResult(runtime, result);
                        return;
                    }
                }

                if (string.IsNullOrEmpty(FallbackValue))
                {
                    GenerateResult(runtime);
                    return;
                }

                GenerateResult(runtime, this.Resolve(owner => owner.FallbackValue));
            });
        }
    }
}
