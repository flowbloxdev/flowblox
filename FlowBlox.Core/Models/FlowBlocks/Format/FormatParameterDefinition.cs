using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Format
{
    [Display(Name = "FormatParameterDefinition", ResourceType = typeof(FlowBloxTexts))]
    public class FormatParameterDefinition : FieldRequiredDefinitionBase
    {
        public FormatParameterDefinition()
        {
            this.IsRequired = true;
        }

        [Required]
        [Display(Name = "Global_FieldElement", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxComboBox]
        [FlowBloxItemsSource(
            DisplayMember = nameof(FieldElement.FullyQualifiedName),
            Method = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public override FieldElement Field { get; set; }

        [Display(Name = "FormatParameterDefinition_IsRequired", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public override bool IsRequired { get; set; }

        public override string ToString() => Field?.ToString() ?? string.Empty;
    }
}
