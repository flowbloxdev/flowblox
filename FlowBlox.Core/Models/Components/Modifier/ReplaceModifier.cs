using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Components.Modifier
{
    [Display(Name = "ReplaceModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class ReplaceModifier : ModifierBase
    {
        [Required]
        [Display(Name = "ReplaceModifier_Search", Order = 0, ResourceType = typeof(FlowBloxTexts))]
        public string Search { get; set; }

        [Required]
        [Display(Name = "ReplaceModifier_Replace", Order = 1, ResourceType = typeof(FlowBloxTexts))]
        public string Replace { get; set; }

        public override string Modify(BaseRuntime runtime, string value)
        {
            string search = this.Resolve(owner => owner.Search);
            string replace = this.Resolve(owner => owner.Replace);
            var replaced = value.Replace(search, replace);
            return replaced;
        }

        public override string ToString()
        {
            var format = FlowBloxResourceUtil.GetLocalizedString(nameof(ReplaceModifier), nameof(ObjectDisplayName));

            return string.Format(format, 
                !string.IsNullOrEmpty(this.Search) ? this.Search : "?", 
                !string.IsNullOrEmpty(this.Replace) ? this.Replace : "?");
        }
    }
}

