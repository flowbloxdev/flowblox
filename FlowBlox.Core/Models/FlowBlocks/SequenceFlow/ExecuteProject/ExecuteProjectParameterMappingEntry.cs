using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.SequenceFlow.ExecuteProject
{
    [Display(Name = "ExecuteProjectParameterMappingEntry_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class ExecuteProjectParameterMappingEntry : FlowBloxReactiveObject
    {
        [Required]
        [Display(Name = "ExecuteProjectParameterMappingEntry_TargetType", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public ExecuteProjectTargetType TargetType { get; set; } = ExecuteProjectTargetType.UserField;

        [Required]
        [Display(Name = "ExecuteProjectParameterMappingEntry_TargetKey", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string TargetKey { get; set; }

        [Required]
        [Display(Name = "ExecuteProjectParameterMappingEntry_TargetValue", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public string TargetValue { get; set; }

        public override string ToString() => $"{TargetType}: {TargetKey}";
    }
}
