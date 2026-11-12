using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Fields;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Components.Modifier
{
    [Display(Name = "LineExtractorModifier_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class LineExtractorModifier : ModifierBase
    {
        [Required]
        [Display(Name = "LineExtractorModifier_StartLine", Order = 0, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int StartLine { get; set; }

        [Display(Name = "LineExtractorModifier_LineCount", Order = 1, ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int? LineCount { get; set; }

        public override string Modify(BaseRuntime runtime, string value)
        {
            var lines = value.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var startLine = this.Resolve(modifier => modifier.StartLine);
            var lineCount = this.Resolve(modifier => modifier.LineCount);
            if (lineCount.HasValue)
            {
                return string.Join(Environment.NewLine, lines.Skip(startLine).Take(lineCount.Value));
            }
            else
            {
                return string.Join(Environment.NewLine, lines.Skip(startLine));
            }
        }

        public override string ToString()
        {
            var format = FlowBloxResourceUtil.GetLocalizedString(nameof(LineExtractorModifier), nameof(ObjectDisplayName));
            return string.Format(format, this.StartLine, this.LineCount.HasValue ? this.StartLine + this.LineCount.Value : "*");
        }
    }
}

