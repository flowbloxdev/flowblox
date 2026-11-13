using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Runtime;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.Core.Models.Components.Modifier
{
    [Display(Name = "RegexMatchModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class RegexMatchModifier : RegexModifierBase
    {
        [Display(Name = "RegexMatchModifier_Behavior", Order = 1, ResourceType = typeof(FlowBloxTexts))]
        public RegexMatchModifierBehavior Behavior { get; set; }

        [Display(Name = "RegexMatchModifier_Separator", Order = 2, ResourceType = typeof(FlowBloxTexts))]
        public string Separator { get; set; }

        public override string Modify(BaseRuntime runtime, string value)
        {
            string search = this.Resolve(owner => owner.Search);
            Regex regex = new Regex(search);
            MatchCollection matches = regex.Matches(value);
            var behavior = this.Resolve(modifier => modifier.Behavior);
            if (matches.Count > 0)
            {
                var separator = Separator == null
                    ? FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.ModifierDefaultSeparator].Value
                    : this.Resolve(owner => owner.Separator);
                Dictionary<string, bool> valueMap = new Dictionary<string, bool>();
                StringBuilder valueBuilder = new StringBuilder();
                foreach (Match match in matches)
                {
                    if ((behavior != RegexMatchModifierBehavior.Distinct) || !valueMap.ContainsKey(match.Value))
                    {
                        valueBuilder.Append(match.Value);
                        valueBuilder.Append(separator);
                        valueMap[match.Value] = true;
                        if (behavior == RegexMatchModifierBehavior.First)
                        {
                            break;
                        }
                    }
                }
                string modifiedValue = valueBuilder.ToString();
                if (modifiedValue.EndsWith(separator)) modifiedValue = modifiedValue.Remove(modifiedValue.Length - separator.Length);
                return modifiedValue.Trim();
            }
            else
            {
                return string.Empty;
            }
        }

        public override string ToString()
        {
            var format = FlowBloxResourceUtil.GetLocalizedString(nameof(RegexMatchModifier), nameof(ObjectDisplayName));

            return string.Format(format,
                !string.IsNullOrEmpty(this.Search) ? this.Search : "?",
                this.Resolve(x => x.Behavior).GetDisplayName());
        }
    }
}

