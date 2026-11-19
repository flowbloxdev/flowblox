using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.IO;

public enum PdfOcrMode
{
    [Display(Name = "PdfOcrMode_EmptyPages", ResourceType = typeof(FlowBloxTexts))]
    EmptyPages,

    [Display(Name = "PdfOcrMode_AllPages", ResourceType = typeof(FlowBloxTexts))]
    AllPages
}
