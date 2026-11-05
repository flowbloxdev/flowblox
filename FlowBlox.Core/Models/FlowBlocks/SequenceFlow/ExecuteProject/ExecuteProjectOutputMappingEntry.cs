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
        [FlowBloxUI(Factory = UIFactory.ComboBox,
            SelectionFilterMethod = nameof(ProjectOutputIteratorFlowBlock.GetOutputPropertyNames))]
        [FlowBloxComboBox(DropDownMode = DropDownMode.Suggestion)]
        public string OutputPropertyName { get; set; }

        [Required]
        [Display(Name = "Global_FieldElement", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Association,  Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public FieldElement Field { get; set; }

        public override string ToString() => $"{OutputPropertyName} → {Field}";
    }
}
