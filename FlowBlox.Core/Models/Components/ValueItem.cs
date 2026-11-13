using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Components
{
    [Display(Name = "ValueItem_DisplayName", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    public class ValueItem : FlowBloxReactiveObject
    {
        [Display(Name = "ValueItem_DisplayNameProperty", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string DisplayName { get; set; }

        [Required()]
        [Display(Name = "ValueItem_Value", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string Value { get; set; }

        public override string ToString() =>
            !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Value ?? string.Empty;
    }
}

