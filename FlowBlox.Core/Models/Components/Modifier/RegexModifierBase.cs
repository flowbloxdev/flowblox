using FlowBlox.Core.Attributes;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Components.Modifier
{
    public abstract class RegexModifierBase : ModifierBase
    {
        [Required]
        [Display(Name = "RegexMatchModifier_Search", Order = 0, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.Regex))]
        [FlowBloxTextBox(IsCodingMode = true)]
        public string Search { get; set; }
    }
}
