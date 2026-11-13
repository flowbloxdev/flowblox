using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AWS
{
    [Display(Name = "InputImageMappingEntry_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public sealed class InputImageMappingEntry : FlowBloxReactiveObject, IValidatableObject
    {
        [Required]
        [Display(Name = "InputImageMappingEntry_FieldElement", Description = "InputImageMappingEntry_FieldElement_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement FieldElement { get; set; }

        [Required]
        [Display(Name = "InputImageMappingEntry_Key", Description = "InputImageMappingEntry_Key_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string Key { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (FieldElement == null)
                yield return new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("InputImageMappingEntry_Validation_FieldElementRequired", typeof(FlowBloxTexts)), [nameof(FieldElement)]);

            if (string.IsNullOrWhiteSpace(Key))
                yield return new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("InputImageMappingEntry_Validation_KeyRequired", typeof(FlowBloxTexts)), [nameof(Key)]);
        }

        public override string ToString()
        {
            var notSetText = FlowBloxResourceUtil.GetLocalizedString("InputImageMappingEntry_NotSet", typeof(FlowBloxTexts));
            return $"{Key ?? notSetText} => {FieldElement?.FullyQualifiedName ?? notSetText}";
        }
    }
}
