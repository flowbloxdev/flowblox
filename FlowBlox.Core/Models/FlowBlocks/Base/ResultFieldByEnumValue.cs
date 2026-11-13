using FlowBlox.Core.Attributes;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Base
{
    public class ResultFieldByEnumValue<TEnum> : FlowBloxReactiveObject
        where TEnum : struct, Enum
    {
        [Display(Name = "Global_ResultField", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [Required]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        [FlowBloxItemsSource(DisplayMember = nameof(FieldElement.Name))]
        public FieldElement ResultField { get; set; }

        [Required]
        [Display(Name = "ResultFieldByEnumValue_EnumValue", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public TEnum? EnumValue { get; set; }

        public override string ToString()
        {
            var notSetText = FlowBloxResourceUtil.GetLocalizedString("ResultFieldByEnumValue_NotSet", typeof(FlowBloxTexts));
            var format = FlowBloxResourceUtil.GetLocalizedString("ResultFieldByEnumValue_ObjectDisplayName", typeof(FlowBloxTexts));

            var enumValue = EnumValue?.ToString() ?? notSetText;
            var resultField = ResultField?.FullyQualifiedName ?? notSetText;

            return string.Format(format, enumValue, resultField);
        }
    }
}
