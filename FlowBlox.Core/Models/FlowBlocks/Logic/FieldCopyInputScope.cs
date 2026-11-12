using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Logic
{
    public enum FieldCopyInputScope
    {
        [Display(Name = "FieldCopyInputScope_DirectPredecessors", ResourceType = typeof(FlowBloxTexts))]
        DirectPredecessors,
        [Display(Name = "FieldCopyInputScope_AllFields", ResourceType = typeof(FlowBloxTexts))]
        AllFields,
        [Display(Name = "FieldCopyInputScope_InputFields", ResourceType = typeof(FlowBloxTexts))]
        InputFields,
        [Display(Name = "FieldCopyInputScope_MemoryFields", ResourceType = typeof(FlowBloxTexts))]
        MemoryFields
    }
}