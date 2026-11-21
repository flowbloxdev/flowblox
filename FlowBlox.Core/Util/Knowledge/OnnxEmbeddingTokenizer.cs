using System.Text.Json;
using FlowBlox.Core.Models.FlowBlocks.AI.OnnxQA;

namespace FlowBlox.Core.Util.Knowledge;

internal sealed class OnnxEmbeddingTokenizer
{
    private readonly OnnxQAModelTokenizer _tokenizer;
    private readonly int _startToken;
    private readonly int _endToken;

    internal OnnxEmbeddingTokenizer(string folder)
    {
        _tokenizer = OnnxQAModelTokenizer.Load(folder);
        _startToken = _tokenizer.ClassificationTokenId;
        _endToken = _tokenizer.SeparatorTokenId;
        using var sentenceConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "sentence_bert_config.json")));
        MaximumTokens = Math.Min(_tokenizer.MaximumSequenceLength, sentenceConfig.RootElement.GetProperty("max_seq_length").GetInt32());
        if (MaximumTokens < 4)
            throw new InvalidDataException("The embedding model sequence length must be at least four tokens.");
    }

    internal int MaximumTokens { get; }

    internal long[] Encode(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var tokens = _tokenizer.EncodeTokens(text);
        if (tokens.Count > MaximumTokens - 2)
            throw new ArgumentException($"Text exceeds the embedding model's {MaximumTokens - 2} content-token limit. Split source text before embedding.");

        return [_startToken, .. tokens.Select(token => (long)token.Id), _endToken];
    }

    internal IReadOnlyList<string> SplitText(string content, int size, int overlap)
    {
        var result = new List<string>();
        foreach (var chunk in KnowledgeTextChunks.Split(content, size, overlap))
        {
            var start = 0;
            while (start < chunk.Length)
            {
                var remaining = chunk[start..];
                var tokens = _tokenizer.EncodeTokens(remaining);
                var end = tokens.Count <= MaximumTokens - 2
                    ? chunk.Length
                    : start + tokens[MaximumTokens - 2].Offset.Start.Value;
                if (end <= start)
                    throw new InvalidDataException("The tokenizer cannot split this source into supported embedding windows.");

                // Retokenizing a substring can change boundary tokens; never silently truncate.
                while (_tokenizer.EncodeTokens(chunk[start..end]).Count > MaximumTokens - 2)
                    end = start + (end - start) / 2;

                var text = chunk[start..end];
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
                if (end == chunk.Length)
                    break;

                start = end - Math.Min(overlap, (end - start) / 4);
                if (start < chunk.Length && char.IsLowSurrogate(chunk[start]))
                    start++;
            }
        }
        return result;
    }
}
