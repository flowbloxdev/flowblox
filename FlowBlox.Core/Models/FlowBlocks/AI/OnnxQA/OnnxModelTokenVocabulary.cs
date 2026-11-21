using Microsoft.ML.Tokenizers;
using System.Text.Json;

namespace FlowBlox.Core.Models.FlowBlocks.AI.OnnxQA;

internal sealed class OnnxModelTokenVocabulary
{
    private readonly Dictionary<string, int> _vocabulary;
    private readonly int _nativeUnknownTokenId;
    private readonly int _modelUnknownTokenId;

    internal OnnxModelTokenVocabulary(
        Dictionary<string, int> vocabulary,
        int nativeUnknownTokenId,
        string unknownToken)
    {
        _vocabulary = vocabulary;
        _nativeUnknownTokenId = nativeUnknownTokenId;
        _modelUnknownTokenId = GetRequiredTokenId(unknownToken);
    }

    internal static OnnxModelTokenVocabulary Load(
        string folder,
        int nativeUnknownTokenId,
        string unknownToken)
    {
        var path = Path.Combine(folder, "tokenizer.json");
        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var modelVocabulary = document.RootElement.GetProperty("model").GetProperty("vocab");
        var vocabulary = modelVocabulary.ValueKind == JsonValueKind.Array
            ? modelVocabulary.EnumerateArray()
                .Select((item, index) => (Token: item[0].GetString()!, Id: index))
                .ToDictionary(item => item.Token, item => item.Id, StringComparer.Ordinal)
            : modelVocabulary.EnumerateObject()
                .ToDictionary(item => item.Name, item => item.Value.GetInt32(), StringComparer.Ordinal);

        if (document.RootElement.TryGetProperty("added_tokens", out var addedTokens))
        {
            foreach (var token in addedTokens.EnumerateArray())
                vocabulary[token.GetProperty("content").GetString()!] = token.GetProperty("id").GetInt32();
        }

        return new OnnxModelTokenVocabulary(vocabulary, nativeUnknownTokenId, unknownToken);
    }

    internal int GetRequiredTokenId(string token)
    {
        if (_vocabulary.TryGetValue(token, out var id))
            return id;

        throw new InvalidDataException($"Required tokenizer token '{token}' is missing from tokenizer.json.");
    }

    internal int GetTokenId(EncodedToken token)
    {
        // Added special tokens can share an ID with SentencePiece's native unknown token.
        if (_vocabulary.TryGetValue(token.Value, out var id))
            return id;

        // SentencePiece preserves the original unknown text in Value. Its ID identifies UNK.
        if (token.Id == _nativeUnknownTokenId)
            return _modelUnknownTokenId;

        throw new InvalidDataException(
            $"Tokenizer output with native ID {token.Id} is missing from tokenizer.json. Use matching tokenizer and model files.");
    }
}
