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
        public string XPath { get; set; }

        [Display(Name = "XmlAssignment_Value", ResourceType = typeof(FlowBloxTexts))]
        public string Value { get; set; }

        [Display(Name = "XmlAssignment_FieldValue", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxComboBox]
        [FlowBloxItemsSource(
            DisplayMember = nameof(FieldElement.FullyQualifiedName),
            Method = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public FieldElement FieldValue { get; set; }

        public override string ToString() => XPath ?? string.Empty;
    }
}
