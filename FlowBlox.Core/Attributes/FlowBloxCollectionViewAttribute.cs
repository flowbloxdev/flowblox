namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Shared presentation settings for list and grid editors.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public abstract class FlowBloxCollectionViewAttribute : Attribute
    {
        /// <summary>
        /// Item properties displayed as columns. An empty list keeps the editor's existing default:
        /// grids show eligible properties; list views show only the item icon.
        /// </summary>
        public string[] ColumnMemberNames { get; set; } = [];

        /// <summary>
        /// Enables moving items up and down in the underlying collection.
        /// Grids disable column sorting while manual item ordering is enabled.
        /// </summary>
        public bool IsMovable { get; set; }
    }
}
