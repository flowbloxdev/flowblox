using FlowBlox.Core.Attributes;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.SequenceFlow.ExecuteProject
{
    [Display(Name = "ExecuteProjectOutputMappingEntry_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class ExecuteProjectOutputMappingEntry : FlowBloxReactiveObject
    {
        [Required]
        [Display(Name = "ExecuteProjectOutputMappingEntry_OutputPropertyName", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxComboBox(DropDownMode = DropDownMode.Suggestion)]
        [FlowBloxItemsSource(Method = nameof(ProjectOutputIteratorFlowBlock.GetOutputPropertyNames))]
        [FlowBloxUI(UISelectionButtons.None)]
        public string OutputPropertyName { get; set; }

        [Required]
        [Display(Name = "Global_FieldElement", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public FieldElement Field { get; set; }

        public override string ToString() => $"{OutputPropertyName} → {Field}";
    }
}
