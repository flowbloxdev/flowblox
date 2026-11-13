using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    public class SplitSeparatorDefinition : FlowBloxReactiveObject
    {
        [Display(Name = "SplitSeparatorDefinition_Separator", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [CustomValidation(typeof(SplitSeparatorDefinition), nameof(ValidateSeparator))]
        public string Separator { get; set; }

        [Display(Name = "SplitSeparatorDefinition_SpecialSeparator", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [CustomValidation(typeof(SplitSeparatorDefinition), nameof(ValidateSeparator))]
        public SpecialSeparator? SpecialSeparator { get; set; }

        public string ResolveSeparator()
        {
            var specialSeparator = this.Resolve(definition => definition.SpecialSeparator);
            if (specialSeparator == Enums.SpecialSeparator.Tab)
                return "\t";

            if (specialSeparator == Enums.SpecialSeparator.NewLine)
                return Environment.NewLine;

            return this.Resolve(owner => owner.Separator);
        }

        public override string ToString() =>
            this.Resolve(x => x.SpecialSeparator)?.ToString() ?? Separator ?? string.Empty;

        public static ValidationResult ValidateSeparator(object value, ValidationContext validationContext)
        {
            var separatorDefinition = (SplitSeparatorDefinition)validationContext.ObjectInstance;
            var separator = separatorDefinition.Resolve(owner => owner.Separator);
            var specialSeparator = separatorDefinition.Resolve(definition => definition.SpecialSeparator);

            if (string.IsNullOrEmpty(separator) && !specialSeparator.HasValue)
                return new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("SplitSeparatorDefinition_Validation_NoSeparatorDefined"), [validationContext.MemberName]);

            if (!string.IsNullOrEmpty(separator) && specialSeparator.HasValue)
                return new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("SplitSeparatorDefinition_Validation_ManySeparatorsDefined"), [validationContext.MemberName]);

            return ValidationResult.Success;
        }
    }
}
