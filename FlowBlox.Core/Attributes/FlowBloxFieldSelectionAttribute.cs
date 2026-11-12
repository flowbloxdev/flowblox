using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class FlowBloxFieldSelectionAttribute : Attribute
    {
        public bool DefaultRequiredValue { get; set; } = true;

        public bool HideRequiredCheckbox { get; set; } = false;

        public string AllowedFieldSelectionModesMethod { get; set; }

        public FieldSelectionModes AllowedFieldSelectionModes { get; set; } = FieldSelectionModes.Default;
    }
}
