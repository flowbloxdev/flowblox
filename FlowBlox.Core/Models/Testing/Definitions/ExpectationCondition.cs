using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Testing
{
    public class ExpectationCondition : ComparisonCondition
    {
        private ExpectationConditionTarget _expectationConditionTarget;

        [Display(Name = "ExpectationCondition_ExpectationConditionTarget", Description = "ExpectationCondition_ExpectationConditionTarget_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public ExpectationConditionTarget ExpectationConditionTarget
        {
            get => _expectationConditionTarget;
            set
            {
                if (_expectationConditionTarget != value)
                {
                    _expectationConditionTarget = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _index;

        [ActivationCondition(MemberName = nameof(ExpectationConditionTarget), Value = ExpectationConditionTarget.ValueAtIndex)]
        [Display(Name = "ExpectationCondition_Index", Description = "ExpectationCondition_Index_Description", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    _index = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public override string ToString()
        {
            var targetText = ExpectationConditionTarget.GetDisplayName();

            if (ExpectationConditionTarget == ExpectationConditionTarget.ValueAtIndex)
                targetText = $"{targetText} {Index}";

            return $"{targetText} {base.ToString()}";
        }
    }
}
