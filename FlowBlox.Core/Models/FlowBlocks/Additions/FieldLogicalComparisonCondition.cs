using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Additions
{
    [Display(Name = "FieldCondition_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class FieldLogicalComparisonCondition : LogicalComparisonCondition
    {
        [JsonIgnore]
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.compare, 16, new SKColor(30, 136, 229));

        [JsonIgnore]
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.compare, 32, new SKColor(30, 136, 229));

        private FieldElement _fieldElement;

        [Required]
        [CustomValidation(typeof(FieldComparisonCondition), nameof(FieldComparisonCondition.ValidateFieldElement))]
        [DependsOnProperty(MemberName = nameof(Operator))]
        [Display(Name = "Global_FieldElement", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.ComboBox,
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            SelectionFilterMethod = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public FieldElement FieldElement
        {
            get => _fieldElement;
            set
            {
                _fieldElement = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ShortDisplayName));
            }
        }

        public override bool Check() => ComparisonCore.CheckField(Operator, Value, FieldElement);

        public override string DisplayName => GetDisplayName(f => f?.FullyQualifiedName);

        public override string ShortDisplayName => GetDisplayName(f => f?.Name);

        private string GetDisplayName(Func<FieldElement, string> selector)
        {
            string baseText = GetComparisonDisplayName();

            if (!string.IsNullOrEmpty(baseText) && char.IsUpper(baseText[0]))
                baseText = char.ToLower(baseText[0]) + baseText.Substring(1);

            string name = selector(FieldElement);

            return !string.IsNullOrEmpty(name)
                ? $"{name} {baseText}"
                : baseText;
        }
    }
}
