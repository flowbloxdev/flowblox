using FlowBlox.Core.Attributes;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Base;

namespace FlowBlox.Core.Models.Components.Modifier
{
    public enum ModifierType
    {
        None,
        SplitModifier,
        ReplaceModifier,
        RegexModifier,
        RegexReplaceModifier,
        SubstringModifier
    };

    public enum RegexMatchModifierBehavior
    {
        [Display(Name = "RegexMatchModifierBehavior_All", ResourceType = typeof(FlowBloxTexts))]
        All,
        [Display(Name = "RegexMatchModifierBehavior_Distinct", ResourceType = typeof(FlowBloxTexts))]
        Distinct,
        [Display(Name = "RegexMatchModifierBehavior_First", ResourceType = typeof(FlowBloxTexts))]
        First
    };

    public abstract class ModifierBase : FlowBloxReactiveObject
    {
        [Required]
        [Display(Name = "PropertyNames_Name", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Visible = false)]
        public string ObjectDisplayName => this.ToString();

        public abstract string Modify(BaseRuntime runtime, string value);

        public abstract override string ToString();
    }
}
