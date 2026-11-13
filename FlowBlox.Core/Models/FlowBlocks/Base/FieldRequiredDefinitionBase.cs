using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;

namespace FlowBlox.Core.Models.FlowBlocks.Base
{
    public abstract class FieldRequiredDefinitionBase : FlowBloxReactiveObject
    {
        public abstract FieldElement Field { get; set; }

        [FlowBloxUI(UISelectionButtons.None)]
        public abstract bool IsRequired { get; set; }
    }
}
