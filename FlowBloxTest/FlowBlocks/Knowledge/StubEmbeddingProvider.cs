using FlowBlox.Core.Models.Components.Knowledge;

namespace FlowBloxTest.FlowBlocks.Knowledge;

public sealed class StubEmbeddingProvider : TextEmbeddingProvider
{
    public List<IReadOnlyList<string>> Calls { get; } = [];
    public bool FailNext { get; set; }

    public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new HttpRequestException("Simulated embedding outage.");
        }

        Calls.Add(texts.ToArray());
        IReadOnlyList<float[]> vectors = texts.Select(text =>
            text.Contains("TBS", StringComparison.OrdinalIgnoreCase) ? new float[] { 1, 0 } : new float[] { 0, 1 }).ToList();
        return Task.FromResult(vectors);
    }
}
