namespace FlowBlox.Core.Util.Knowledge;

internal static class KnowledgeTextChunks
{
    internal static IReadOnlyList<string> Split(string content, int size, int overlap)
    {
        if (size < 64 || overlap < 0 || overlap >= size)
            throw new ArgumentException("Chunk size must be at least 64 characters and overlap must be smaller than the chunk size.");

        var chunks = new List<string>();
        var start = 0;
        while (start < content.Length)
        {
            var end = Math.Min(start + size, content.Length);
            if (end < content.Length && char.IsHighSurrogate(content[end - 1]))
                end--;

            var chunk = content[start..end];
            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            if (end == content.Length)
                break;

            start = Math.Max(start + 1, end - overlap);
            if (start < content.Length && char.IsLowSurrogate(content[start]))
                start++;
        }

        return chunks;
    }
}
