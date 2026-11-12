using FlowBlox.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Models.Components.Modifier
{
    [Display(Name = "RegexReplaceModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class RegexReplaceModifier : RegexModifierBase
    {
        [Display(Name = "RegexMatchModifier_Replace", Order = 1, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.Regex), UiOptions = UIOptions.EnableFieldSelection)]
        public string Replace { get; set; }

        public override string Modify(BaseRuntime runtime, string value)
        {
            string search = this.Resolve(owner => owner.Search);
            string replace = this.Resolve(owner => owner.Replace);
            Regex regex = new Regex(search);
            var result = regex.Replace(value, replace ?? string.Empty);
            return result;
        }

        public override string ToString()
        {
            var format = FlowBloxResourceUtil.GetLocalizedString(nameof(RegexReplaceModifier), nameof(ObjectDisplayName));

            return string.Format(format,
                !string.IsNullOrEmpty(this.Search) ? this.Search : "?",
                !string.IsNullOrEmpty(this.Replace) ? this.Replace : string.Empty);
        }
    }
}

