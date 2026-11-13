using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Parsing;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Additions
{
    [Display(Name = "ComparisonCondition_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class ComparisonCondition : FlowBloxReactiveObject
    {
        [JsonIgnore]
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.compare, 16, new SKColor(30, 136, 229)); // Blue

        [JsonIgnore]
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.compare, 32, new SKColor(30, 136, 229));


        private ComparisonOperator _operator;
        private string _value;

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

        [JsonIgnore]
        [DeepCopierIgnore]
        internal ComparisonCore Comparison { get; } = new ComparisonCore();

        public virtual bool Compare(object leftValue) => Compare(leftValue, null);

        public virtual bool Compare(object leftValue, IValueParsingStrategy parsingStrategy) => Comparison.Check(Operator, Value, leftValue, parsingStrategy);

        [Display(Name = "PropertyNames_Name", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Visible = false, ReadOnly = true)]
        public virtual string DisplayName
        {
            get
            {
                if (Operator == ComparisonOperator.HasValue || 
                    Operator == ComparisonOperator.HasNoValue)
                    return Operator.GetDisplayName();

                return $"{Operator.GetDisplayName()} \"{Value}\"";
            }
        }

        public virtual string ShortDisplayName => DisplayName;

        public override string ToString() => DisplayName;
    }
}
