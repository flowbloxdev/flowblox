using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.Core.Models.Components.Modifier
{
    [Display(Name = "SubstringModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class SubstringModifier : ModifierBase
    {
        [Required()]
        [Display(Name = "SubstringModifier_Index", Order = 0, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int Index { get; set; }

        [Required()]
        [Display(Name = "SubstringModifier_Length", Order = 1, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int Length { get; set; }

        public override string Modify(BaseRuntime runtime, string value)
        {
            var index = this.Resolve(modifier => modifier.Index);
            var length = this.Resolve(modifier => modifier.Length);
            return value.Substring(index, length);
        }

        public override string ToString()
        {
            var format = FlowBloxResourceUtil.GetLocalizedString(nameof(SubstringModifier), nameof(ObjectDisplayName));
            return string.Format(format, this.Index, this.Index + this.Length);
        }
    }
}

