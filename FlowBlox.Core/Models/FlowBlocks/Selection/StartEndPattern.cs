using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Selection
{
    public class StartEndPattern : FlowBloxReactiveObject
    {
        [Required]
        [Display(Name = "StartEndPattern_StartPattern", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string StartPattern { get; set; }

        [Display(Name = "StartEndPattern_EndPattern", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string EndPattern { get; set; }

        [Display(Name = "StartEndPattern_Index", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int? Index { get; set; }

        [Display(Name = "StartEndPattern_ReturnOptions", Description = "StartEndPattern_ReturnOptions_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public StartEndPatternReturnOptions? ReturnOptions { get; set; }

        public override string ToString() => string.IsNullOrWhiteSpace(EndPattern)
            ? StartPattern ?? string.Empty
            : $"{StartPattern} … {EndPattern}";
    }
}
