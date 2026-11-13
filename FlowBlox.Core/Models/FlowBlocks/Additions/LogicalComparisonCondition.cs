using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Parsing;
using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Additions
{
    [Display(Name = "LogicalComparisonCondition_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public abstract class LogicalComparisonCondition : LogicalCondition
    {
        private ComparisonOperator _operator;
        private string _value;

        [JsonIgnore]
        [DeepCopierIgnore]
        internal ComparisonCore ComparisonCore { get; } = new ComparisonCore();

        [Display(Name = "ComparisonCondition_Operator", ResourceType = typeof(FlowBloxTexts), Order = 10)]
        [FlowBloxUI(UISelectionButtons.None)]
        public ComparisonOperator Operator
        {
            get => _operator;
            set
            {
                _operator = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ShortDisplayName));
            }
        }

        [Display(Name = "ComparisonCondition_Value", ResourceType = typeof(FlowBloxTexts), Order = 20)]
        [FlowBloxTextBox(MultiLine = true)]
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ShortDisplayName));
            }
        }

        public virtual bool Compare(object leftValue) => Compare(leftValue, null);

        public virtual bool Compare(object leftValue, IValueParsingStrategy parsingStrategy) => ComparisonCore.Check(Operator, Value, leftValue, parsingStrategy);

        protected string GetComparisonDisplayName()
        {
            if (Operator == ComparisonOperator.HasValue || 
                Operator == ComparisonOperator.HasNoValue)
                return Operator.GetDisplayName();

            return $"{Operator.GetDisplayName()} \"{Value}\"";
        }
    }
}
