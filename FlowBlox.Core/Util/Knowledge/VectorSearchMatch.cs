namespace FlowBlox.Core.Util.Knowledge;

internal sealed class VectorSearchMatch
{
    internal string SourceId { get; init; }
    internal int ChunkIndex { get; init; }
    internal string Revision { get; init; }
    internal double Score { get; init; }
}
