using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Enums;

public enum FieldDisplayMode
{
    [Display(Name = "FieldDisplayMode_SingleLine", ResourceType = typeof(FlowBloxTexts))]
    SingleLine,

    [Display(Name = "FieldDisplayMode_Multiline", ResourceType = typeof(FlowBloxTexts))]
    Multiline,

    [Display(Name = "FieldDisplayMode_List", ResourceType = typeof(FlowBloxTexts))]
    List
}
