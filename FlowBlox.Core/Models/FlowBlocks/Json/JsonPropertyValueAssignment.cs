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
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string PropertyName { get; set; }

        [Display(Name = "JsonPropertyValueAssignment_Value", Description = "JsonPropertyValueAssignment_Value_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public string Value { get; set; }

        [Display(Name = "JsonPropertyValueAssignment_FieldValue", Description = "JsonPropertyValueAssignment_FieldValue_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.ComboBox, 
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            SelectionFilterMethod = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public FieldElement FieldValue { get; set; }

        [Display(Name = "JsonPropertyValueAssignment_IsArray", Description = "JsonPropertyValueAssignment_IsArray_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool IsArray { get; set; }

        public override string ToString() => PropertyName ?? string.Empty;
    }
}
