using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Base
{
    [Serializable()]
    public class InputBehaviorAssignment : FlowBloxReactiveObject
    {
        [JsonIgnore]
        public string DisplayName => FlowBloxResourceUtil.GetLocalizedString(
            "InputBehaviorAssignment_DisplayName",
            typeof(FlowBloxTexts));

        [Required]
        [Display(Name = "Global_FlowBlock", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(ReadOnly = true)]
        [FlowBloxComboBox]
        public BaseFlowBlock FlowBlock { get; set; }


        [Display(Name = "InputBehaviorAssignment_Behavior", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public InputBehavior Behavior { get; set; } = InputBehavior.First;

        public override string ToString() =>
            string.IsNullOrWhiteSpace(FlowBlock?.Name) ? DisplayName : FlowBlock.Name;
    }
}
