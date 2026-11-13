namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Configures object creation and management for an association, list or grid editor.
    /// Only this attribute without a collection editor selects the single association editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class FlowBloxAssociationAttribute : Attribute
    {
        /// <summary>
        /// Allowed object operations. All operations are enabled by default.
        /// </summary>
        public UIOperations Operations { get; set; } = UIOperations.All;

        /// <summary>
        /// Limits the concrete types offered when creating an abstract or interface value.
        /// </summary>
        public Type[] CreatableTypes { get; set; } = [];

        /// <summary>
        /// Factory implementing IItemFactory&lt;T&gt; with a public parameterless constructor.
        /// Creates the associated value or a collection item.
        /// </summary>
        public Type? ItemFactory { get; set; }
    }
}
