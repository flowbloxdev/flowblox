using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Additions
{
    [Display(Name = "FieldCondition_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class FieldComparisonCondition : ComparisonCondition
    {
        private string _outputValue;

        [Required]
        [CustomValidation(typeof(FieldComparisonCondition), nameof(ValidateFieldElement))]
        [DependsOnProperty(MemberName = nameof(Operator))]
        [Display(Name = "Global_FieldElement", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.ComboBox,
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            SelectionFilterMethod = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public FieldElement FieldElement { get; set; }

        public static ValidationResult ValidateFieldElement(FieldElement fieldElement, ValidationContext validationContext)
        {
            if (fieldElement == null)
                return ValidationResult.Success;

            var op = validationContext.ObjectInstance is FieldComparisonCondition condition
                ? condition.Operator
                : ((FieldLogicalComparisonCondition)validationContext.ObjectInstance).Operator;
            if (fieldElement.FieldType?.FieldType == FieldTypes.ByteArray)
            {
                if (ComparisonCore.SupportsBinaryOperator(op))
                    return ValidationResult.Success;

                var message = FlowBloxResourceUtil.GetLocalizedString("FieldComparisonCondition_Validation_ByteArrayOperatorNotSupported");
                return new ValidationResult(message, [validationContext.MemberName]);
            }

            var fieldValue = fieldElement.Value;
            var isStringField = fieldValue is string || (fieldValue == null
                && (fieldElement.FieldType == null || fieldElement.FieldType.FieldType == FieldTypes.Text));
            if ((op == ComparisonOperator.Contains || op == ComparisonOperator.NotContains) && !isStringField)
            {
                var message = FlowBloxResourceUtil.GetLocalizedString("FieldComparisonCondition_Validation_ContainsRequiresText");
                return new ValidationResult(message, [validationContext.MemberName]);
            }

            return ValidationResult.Success;
        }

        [Display(Name = "FieldComparisonCondition_OutputValue", Description = "FieldComparisonCondition_OutputValue_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 30)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox(MultiLine = true)]
        public string OutputValue
        {
            get => _outputValue;
            set
            {
                _outputValue = value;
                OnPropertyChanged();
            }
        }

        public virtual bool Compare() => Comparison.CheckField(Operator, Value, FieldElement);

        public override string DisplayName => GetDisplayName(f => f?.FullyQualifiedName);

        public override string ShortDisplayName => GetDisplayName(f => f?.Name);

        private string GetDisplayName(Func<FieldElement, string> selector)
        {
            string baseText = base.DisplayName;

            if (!string.IsNullOrEmpty(baseText) && char.IsUpper(baseText[0]))
                baseText = char.ToLower(baseText[0]) + baseText.Substring(1);

            string name = selector(FieldElement);

            return !string.IsNullOrEmpty(name)
                ? $"{name} {baseText}"
                : baseText;
        }
    }
}
