using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Base;

public enum OutputSortDirection
{
    [Display(Name = "OutputSortDirection_None", ResourceType = typeof(FlowBloxTexts))]
    None,
    [Display(Name = "OutputSortDirection_Ascending", ResourceType = typeof(FlowBloxTexts))]
    Ascending,
    [Display(Name = "OutputSortDirection_Descending", ResourceType = typeof(FlowBloxTexts))]
    Descending
}
