namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Common property presentation settings. Editor-specific attributes select and configure the editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class FlowBloxUIAttribute : System.ComponentModel.DataAnnotations.ValidationAttribute
    {
        /// <summary>
        /// Creates general UI settings with the standard field-selection buttons.
        /// </summary>
        public FlowBloxUIAttribute()
        {
        }

        /// <summary>
        /// Creates general UI settings with an explicit selection-button combination.
        /// Use None to disable all selection buttons.
        /// </summary>
        public FlowBloxUIAttribute(UISelectionButtons selectionButtons)
        {
            SelectionButtons = selectionButtons;
        }

        /// <summary>
        /// Shows the property's label. Enabled by default.
        /// </summary>
        public bool DisplayLabel { get; set; } = true;

        /// <summary>
        /// Includes the property in the generated UI. Enabled by default.
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Registered toolbox category identifier for selectable text templates, for example nameof(FlowBloxToolboxCategory.Regex).
        /// </summary>
        public string ToolboxCategory { get; set; }

        /// <summary>
        /// Public parameterless instance method returning whether the editor is read-only.
        /// </summary>
        public string ReadOnlyMethod { get; set; }

        /// <summary>
        /// Optional selection buttons. Field selection is the default for eligible scalar properties.
        /// Explicit values replace the default; include FLD to retain field selection alongside FL or DI.
        /// </summary>
        public UISelectionButtons SelectionButtons { get; set; } = UISelectionButtons.FLD;

        /// <summary>
        /// Expands the property editor to fill the available space.
        /// </summary>
        public bool Maximize { get; set; }

        /// <summary>
        /// Prevents editing the property through the generated UI.
        /// </summary>
        public bool ReadOnly { get; set; }

        protected override System.ComponentModel.DataAnnotations.ValidationResult IsValid(
            object value,
            System.ComponentModel.DataAnnotations.ValidationContext validationContext)
        {
            // Possible unresolved references are runtime warnings, never UI validation failures.
            return System.ComponentModel.DataAnnotations.ValidationResult.Success;
        }
    }
}
