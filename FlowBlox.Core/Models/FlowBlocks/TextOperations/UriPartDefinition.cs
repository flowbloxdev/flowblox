using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    /// <summary>
    /// A single URI part.
    /// </summary>
    [Display(Name = "ConcatUriFlowBlock_UriPart_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public sealed class UriPartDefinition : FlowBloxReactiveObject
    {
        [Display(Name = "ConcatUriFlowBlock_UriPart_Value", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxTextBox(IsCodingMode = true)]
        public string Value { get; set; }

        public override string ToString() => Value ?? string.Empty;
    }
}
