namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Selects a list editor and configures its columns and manual item ordering.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class FlowBloxListViewAttribute : FlowBloxCollectionViewAttribute
    {
        /// <summary>
        /// Shows the selected item's property editor beside the list.
        /// </summary>
        public bool SplitMode { get; set; }
    }
}
