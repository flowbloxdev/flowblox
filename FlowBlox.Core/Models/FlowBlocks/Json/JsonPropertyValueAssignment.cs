using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Json
{
    [Display(Name = "JsonPropertyValueAssignment_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class JsonPropertyValueAssignment : FlowBloxReactiveObject
    {
        [Display(Name = "JsonPropertyValueAssignment_PropertyName", Description = "JsonPropertyValueAssignment_PropertyName_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        public string PropertyName { get; set; }

        [Display(Name = "JsonPropertyValueAssignment_Value", Description = "JsonPropertyValueAssignment_Value_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        public string Value { get; set; }

        [Display(Name = "JsonPropertyValueAssignment_FieldValue", Description = "JsonPropertyValueAssignment_FieldValue_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxComboBox]
        [FlowBloxItemsSource(
            DisplayMember = nameof(FieldElement.FullyQualifiedName),
            Method = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public FieldElement FieldValue { get; set; }

        [Display(Name = "JsonPropertyValueAssignment_IsArray", Description = "JsonPropertyValueAssignment_IsArray_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        public bool IsArray { get; set; }

        public override string ToString() => PropertyName ?? string.Empty;
    }
}
