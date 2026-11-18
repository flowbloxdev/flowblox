namespace FlowBlox.Core.Models.FlowBlocks.Base;

internal sealed class OutputSortValueComparer : IComparer<object>
{
    public static OutputSortValueComparer Instance { get; } = new();

    public int Compare(object left, object right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return -1;
        if (right == null)
            return 1;
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
        if (left is string leftText && right is string rightText)
            return StringComparer.Ordinal.Compare(leftText, rightText);

        return ((IComparable)left).CompareTo(right);
    }
}
