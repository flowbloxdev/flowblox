namespace FlowBlox.Core.Attributes
{
    public enum DropDownMode
    {
        SelectionOnly,
        Suggestion
    }

    /// <summary>
    /// Selects and configures a combo box editor. Suggestion mode permits
    /// custom scalar values; complex properties always select existing objects.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class FlowBloxComboBoxAttribute : Attribute
    {
        public DropDownMode DropDownMode { get; set; } = DropDownMode.SelectionOnly;

        /// <summary>
        /// Parameterless instance method returning DropDownMode. Resolved on the target or parent.
        /// </summary>
        public string DropDownModeMethod { get; set; }

        /// <summary>
        /// Optional member path mapping each choice to the property's value.
        /// Without a mapping the choice itself is assigned.
        /// </summary>
        public string SelectionValueMember { get; set; }
    }
}
