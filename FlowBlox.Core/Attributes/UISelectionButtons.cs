namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Optional selection buttons for supported property editors. Explicit values replace the default combination.
    /// </summary>
    [Flags]
    public enum UISelectionButtons
    {
        /// <summary>
        /// Disables all optional selection buttons.
        /// </summary>
        None = 0,

        /// <summary>
        /// Selects runtime fields and placeholders for supported scalar values.
        /// </summary>
        FLD = 1,

        /// <summary>
        /// Selects a file for a text property.
        /// </summary>
        FL = 2,

        /// <summary>
        /// Selects a directory for a text property.
        /// </summary>
        DI = 4
    }
}
