namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Supplies selectable entries for association, list and combo box editors.
    /// Does not select an editor or enable field-reference buttons.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class FlowBloxItemsSourceAttribute : Attribute
    {
        /// <summary>
        /// Parameterless instance method returning selectable entries; resolved on the target or its parent.
        /// </summary>
        public string? Method { get; set; }

        /// <summary>
        /// Property whose change notification refreshes the combo box choices.
        /// </summary>
        public string? Dependency { get; set; }

        /// <summary>
        /// Member path used to display each selectable entry, for example nameof(Name).
        /// </summary>
        public string? DisplayMember { get; set; }
    }
}
