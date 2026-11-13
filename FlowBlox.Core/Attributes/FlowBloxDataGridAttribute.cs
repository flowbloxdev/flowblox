namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Selects a grid editor and configures its columns and manual item ordering.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class FlowBloxDataGridAttribute : FlowBloxCollectionViewAttribute
    {
    }
}
