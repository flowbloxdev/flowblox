using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Xml
{
    public class XmlAssignment : FlowBloxReactiveObject
    {
        [Display(Name = "XmlAssignment_XPath", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string XPath { get; set; }

        [Display(Name = "XmlAssignment_Value", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public string Value { get; set; }

        [Display(Name = "XmlAssignment_FieldValue", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.ComboBox,
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            SelectionFilterMethod = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public FieldElement FieldValue { get; set; }

        public override string ToString() => XPath ?? string.Empty;
    }
}
