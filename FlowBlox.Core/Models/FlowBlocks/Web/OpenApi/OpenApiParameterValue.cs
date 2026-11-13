using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Web.OpenApi
{
    public class OpenApiParameterValue : FlowBloxReactiveObject
    {
        [Display(Name = "OpenApiParameterValue_Name", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
        public string Name { get; set; }

        [Display(Name = "OpenApiParameterValue_Location", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
        public string Location { get; set; }

        [Display(Name = "OpenApiParameterValue_Required", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
        public bool Required { get; set; }

        [Display(Name = "OpenApiParameterValue_Description", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
        public string Description { get; set; }

        [Display(Name = "OpenApiParameterValue_Value", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public string Value { get; set; }

        public override string ToString() => Name ?? string.Empty;
    }
}
