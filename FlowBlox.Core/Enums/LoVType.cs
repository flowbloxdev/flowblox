using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Enums
{
    public enum LoVType
    {
        [Display(Name = "LoVType_Suggestion", ResourceType = typeof(FlowBloxTexts))]
        Suggestion,

        [Display(Name = "LoVType_PossibleValues", ResourceType = typeof(FlowBloxTexts))]
        PossibleValues
    }
}
