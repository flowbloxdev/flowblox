using Microsoft.ML.Tokenizers;
using System.Text.Json;

namespace FlowBlox.Core.Models.FlowBlocks.AI.OnnxQA
{
    internal sealed class OnnxQAModelTokenizer
    {
        private readonly Microsoft.ML.Tokenizers.Tokenizer _tokenizer;
        private readonly OnnxQASequenceFormat _sequenceFormat;
        private readonly int _classificationTokenId;
        private readonly int _separatorTokenId;
        private readonly OnnxModelTokenVocabulary _modelVocabulary;

        private OnnxQAModelTokenizer(
            Microsoft.ML.Tokenizers.Tokenizer tokenizer,
            OnnxQASequenceFormat sequenceFormat,
            int classificationTokenId,
            int separatorTokenId,
            int maximumSequenceLength,
            OnnxModelTokenVocabulary modelVocabulary = null)
        {
            _tokenizer = tokenizer;
            _sequenceFormat = sequenceFormat;
            _classificationTokenId = classificationTokenId;
            _separatorTokenId = separatorTokenId;
            MaximumSequenceLength = maximumSequenceLength;
            _modelVocabulary = modelVocabulary;
        }

        public int MaximumSequenceLength { get; }
        internal int ClassificationTokenId => _classificationTokenId;
        internal int SeparatorTokenId => _separatorTokenId;

        public static OnnxQAModelTokenizer Load(string modelFolder)
        {
            var tokenizerConfig = ReadJsonObject(Path.Combine(modelFolder, "tokenizer_config.json"));
            var specialTokensMap = ReadJsonObject(Path.Combine(modelFolder, "special_tokens_map.json"));
            var modelConfig = ReadJsonObject(Path.Combine(modelFolder, "config.json"));
            var modelType = GetString(modelConfig, "model_type")?.ToLowerInvariant();
            var maximumSequenceLength = GetMaximumSequenceLength(tokenizerConfig, modelConfig);
            var sequenceFormat = UsesRobertaSequenceFormat(modelType)
                ? OnnxQASequenceFormat.Roberta
                : OnnxQASequenceFormat.Bert;

            var wordPieceVocabulary = Path.Combine(modelFolder, "vocab.txt");
            if (File.Exists(wordPieceVocabulary))
            {
                var lowerCase = GetBoolean(tokenizerConfig, "do_lower_case", false);
                var options = new BertOptions
                {
                    LowerCaseBeforeTokenization = lowerCase,
                    ApplyBasicTokenization = GetBoolean(tokenizerConfig, "do_basic_tokenize", true),
                    SplitOnSpecialTokens = true,
                    ClassificationToken = GetToken(tokenizerConfig, "cls_token", "[CLS]"),
                    SeparatorToken = GetToken(tokenizerConfig, "sep_token", "[SEP]"),
                    PaddingToken = GetToken(tokenizerConfig, "pad_token", "[PAD]"),
                    MaskingToken = GetToken(tokenizerConfig, "mask_token", "[MASK]"),
                    IndividuallyTokenizeCjk = GetBoolean(tokenizerConfig, "tokenize_chinese_chars", true),
                    RemoveNonSpacingMarks = GetNullableBoolean(tokenizerConfig, "strip_accents") ?? lowerCase
                };

                var tokenizer = BertTokenizer.Create(wordPieceVocabulary, options);
                return new OnnxQAModelTokenizer(
                    tokenizer,
                    OnnxQASequenceFormat.Bert,
                    tokenizer.ClassificationTokenId,
                    tokenizer.SeparatorTokenId,
                    maximumSequenceLength);
            }

            var bpeVocabulary = Path.Combine(modelFolder, "vocab.json");
            var bpeMerges = Path.Combine(modelFolder, "merges.txt");
            if (File.Exists(bpeVocabulary) && File.Exists(bpeMerges))
            {
                var vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(bpeVocabulary))
                    ?? throw new InvalidDataException("The tokenizer vocabulary is empty.");
                var classificationToken = GetToken(tokenizerConfig, "cls_token", "<s>");
                var separatorToken = GetToken(tokenizerConfig, "sep_token", "</s>");
                var paddingToken = GetToken(tokenizerConfig, "pad_token", "<pad>");
                var unknownToken = GetToken(tokenizerConfig, "unk_token", "<unk>");
                var specialTokens = new Dictionary<string, int>();
                AddSpecialToken(specialTokens, vocabulary, classificationToken);
                AddSpecialToken(specialTokens, vocabulary, separatorToken);
                AddSpecialToken(specialTokens, vocabulary, paddingToken);
                AddSpecialToken(specialTokens, vocabulary, unknownToken);

                var options = new BpeOptions(bpeVocabulary, bpeMerges)
                {
                    ByteLevel = true,
                    PreTokenizer = RobertaPreTokenizer.Instance,
                    SpecialTokens = specialTokens,
                    UnknownToken = unknownToken
                };
                var tokenizer = BpeTokenizer.Create(options);

                return new OnnxQAModelTokenizer(
                    tokenizer,
                    sequenceFormat,
                    GetRequiredTokenId(vocabulary, classificationToken),
                    GetRequiredTokenId(vocabulary, separatorToken),
                    maximumSequenceLength);
            }

            var sentencePieceModel = FindSentencePieceModel(modelFolder);
            if (sentencePieceModel != null)
            {
                var classificationFallback = sequenceFormat == OnnxQASequenceFormat.Roberta ? "<s>" : "[CLS]";
                var separatorFallback = sequenceFormat == OnnxQASequenceFormat.Roberta ? "</s>" : "[SEP]";
                var classificationToken = GetToken(tokenizerConfig, specialTokensMap, "cls_token", classificationFallback);
                var separatorToken = GetToken(tokenizerConfig, specialTokensMap, "sep_token", separatorFallback);
                var addedTokens = ReadAddedTokens(modelFolder);

                using var modelStream = File.OpenRead(sentencePieceModel);
                var tokenizer = SentencePieceTokenizer.Create(
                    modelStream,
                    addBeginningOfSentence: false,
                    addEndOfSentence: false,
                    specialTokens: addedTokens);

                var unknownToken = GetToken(tokenizerConfig, specialTokensMap, "unk_token", tokenizer.UnknownToken);
                var modelVocabulary = OnnxModelTokenVocabulary.Load(modelFolder, tokenizer.UnknownId, unknownToken);

                return new OnnxQAModelTokenizer(
                    tokenizer,
                    sequenceFormat,
                    modelVocabulary?.GetRequiredTokenId(classificationToken)
                        ?? GetRequiredTokenId(tokenizer, addedTokens, classificationToken),
                    modelVocabulary?.GetRequiredTokenId(separatorToken)
                        ?? GetRequiredTokenId(tokenizer, addedTokens, separatorToken),
                    maximumSequenceLength,
                    modelVocabulary);
            }

            throw new NotSupportedException(
                "Unsupported tokenizer files. The model folder must contain either vocab.txt (BERT/WordPiece), " +
                "vocab.json and merges.txt (RoBERTa byte-level BPE), or a SentencePiece model " +
                "(spm.model, sentencepiece.bpe.model, tokenizer.model). Include added_tokens.json or " +
                "tokenizer.json when special tokens use IDs outside the SentencePiece vocabulary.");
        }

        public IReadOnlyList<EncodedToken> EncodeTokens(string text)
        {
            if (_tokenizer is SentencePieceTokenizer sentencePieceTokenizer)
            {
                var originalText = text ?? string.Empty;
                var tokens = sentencePieceTokenizer.EncodeToTokens(
                    originalText,
                    out var normalizedText,
                    addBeginningOfSentence: false,
                    addEndOfSentence: false,
                    considerPreTokenization: true,
                    considerNormalization: true);

                var mapped = MapSentencePieceOffsetsToOriginal(originalText, normalizedText, tokens);
                if (_modelVocabulary == null)
                    return mapped;

                return mapped.Select(token => new EncodedToken(
                    _modelVocabulary.GetTokenId(token),
                    token.Value,
                    token.Offset)).ToList();
            }

            return _tokenizer.EncodeToTokens(text ?? string.Empty, out _, considerPreTokenization: true, considerNormalization: true);
        }

        internal static IReadOnlyList<EncodedToken> MapSentencePieceOffsetsToOriginal(
            string originalText,
            string? normalizedText,
            IReadOnlyList<EncodedToken> tokens)
        {
            if (string.IsNullOrEmpty(normalizedText) || tokens.Count == 0 || normalizedText == originalText)
                return tokens;

            var comparable = normalizedText.Replace('\u2581', ' ');
            var dummyPrefixLength = comparable.Length > 0 &&
                                    comparable[0] == ' ' &&
                                    (originalText.Length == 0 || !char.IsWhiteSpace(originalText[0]))
                ? 1
                : 0;
            var normalizedCore = comparable[dummyPrefixLength..];
            var coreBoundaryMap = normalizedCore == originalText
                ? Enumerable.Range(0, normalizedCore.Length + 1).ToArray()
                : BuildNormalizedToOriginalBoundaryMap(normalizedCore, originalText);

            return tokens.Select(token =>
            {
                var normalizedStart = Math.Clamp(token.Offset.Start.Value - dummyPrefixLength, 0, normalizedCore.Length);
                var normalizedEnd = Math.Clamp(token.Offset.End.Value - dummyPrefixLength, 0, normalizedCore.Length);
                var originalStart = coreBoundaryMap[normalizedStart];
                var originalEnd = coreBoundaryMap[normalizedEnd];
                if (token.Value.Length > 1 && token.Value[0] == '\u2581' &&
                    originalStart < originalEnd && char.IsWhiteSpace(originalText[originalStart]))
                    originalStart++;
                return new EncodedToken(token.Id, token.Value, new Range(originalStart, originalEnd));
            }).ToList();
        }

        private static int[] BuildNormalizedToOriginalBoundaryMap(string normalizedText, string originalText)
        {
            var result = new int[normalizedText.Length + 1];
            var normalizedIndex = 0;
            var originalIndex = 0;

            while (normalizedIndex < normalizedText.Length)
            {
                result[normalizedIndex] = originalIndex;
                if (originalIndex >= originalText.Length)
                {
                    normalizedIndex++;
                    result[normalizedIndex] = originalIndex;
                    continue;
                }

                if (CharactersMatch(normalizedText[normalizedIndex], originalText[originalIndex]))
                {
                    normalizedIndex++;
                    originalIndex++;
                    result[normalizedIndex] = originalIndex;
                    continue;
                }

                var matchingOriginalIndex = FindNextMatchingCharacter(
                    originalText,
                    originalIndex + 1,
                    normalizedText[normalizedIndex]);
                var matchingNormalizedIndex = FindNextMatchingCharacter(
                    normalizedText,
                    normalizedIndex + 1,
                    originalText[originalIndex]);

                if (matchingOriginalIndex >= 0 &&
                    (matchingNormalizedIndex < 0 ||
                     matchingOriginalIndex - originalIndex <= matchingNormalizedIndex - normalizedIndex))
                {
                    originalIndex = matchingOriginalIndex;
                    continue;
                }

                if (matchingNormalizedIndex >= 0)
                {
                    normalizedIndex++;
                    result[normalizedIndex] = originalIndex;
                    continue;
                }

                normalizedIndex++;
                originalIndex++;
                result[normalizedIndex] = originalIndex;
            }

            result[^1] = originalText.Length;
            for (var index = 1; index < result.Length; index++)
                result[index] = Math.Max(result[index], result[index - 1]);

            return result;
        }

        private static int FindNextMatchingCharacter(string text, int startIndex, char character)
        {
            var endIndex = Math.Min(text.Length, startIndex + 32);
            for (var index = startIndex; index < endIndex; index++)
            {
                if (CharactersMatch(text[index], character))
                    return index;
            }

            return -1;
        }

        private static bool CharactersMatch(char left, char right)
        {
            return left == right || (char.IsWhiteSpace(left) && char.IsWhiteSpace(right));
        }

        public OnnxQAEncodedWindow BuildWindow(
            IReadOnlyList<EncodedToken> questionTokens,
            IReadOnlyList<EncodedToken> contextTokens,
            int contextTokenOffset,
            int contextTokenCount)
        {
            var isRoberta = _sequenceFormat == OnnxQASequenceFormat.Roberta;
            var specialTokenCount = isRoberta ? 4 : 3;
            var length = questionTokens.Count + contextTokenCount + specialTokenCount;
            var inputIds = new long[length];
            var attentionMask = Enumerable.Repeat(1L, length).ToArray();
            var tokenTypeIds = new long[length];
            var cursor = 0;

            inputIds[cursor++] = _classificationTokenId;
            foreach (var token in questionTokens)
                inputIds[cursor++] = token.Id;

            inputIds[cursor++] = _separatorTokenId;
            if (isRoberta)
                inputIds[cursor++] = _separatorTokenId;

            var contextStartIndex = cursor;
            for (var index = 0; index < contextTokenCount; index++)
            {
                inputIds[cursor] = contextTokens[contextTokenOffset + index].Id;
                tokenTypeIds[cursor] = isRoberta ? 0L : 1L;
                cursor++;
            }

            inputIds[cursor] = _separatorTokenId;
            tokenTypeIds[cursor] = isRoberta ? 0L : 1L;

            return new OnnxQAEncodedWindow(
                inputIds,
                attentionMask,
                tokenTypeIds,
                contextStartIndex,
                contextTokenOffset,
                contextTokenCount);
        }

        public int GetAvailableContextTokenCount(int questionTokenCount, int requestedMaximumSequenceLength)
        {
            var maximumSequenceLength = Math.Min(requestedMaximumSequenceLength, MaximumSequenceLength);
            return maximumSequenceLength - questionTokenCount -
                (_sequenceFormat == OnnxQASequenceFormat.Roberta ? 4 : 3);
        }

        private static bool UsesRobertaSequenceFormat(string? modelType)
        {
            return modelType is "roberta" or "xlm-roberta" or "camembert";
        }

        private static string? FindSentencePieceModel(string modelFolder)
        {
            foreach (var fileName in new[] { "spm.model", "sentencepiece.bpe.model", "tokenizer.model" })
            {
                var path = Path.Combine(modelFolder, fileName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static Dictionary<string, int> ReadAddedTokens(string modelFolder)
        {
            var tokens = new Dictionary<string, int>(StringComparer.Ordinal);
            var addedTokensPath = Path.Combine(modelFolder, "added_tokens.json");
            var addedTokens = ReadJsonObject(addedTokensPath);
            if (addedTokens is { ValueKind: JsonValueKind.Object })
            {
                foreach (var property in addedTokens.Value.EnumerateObject())
                {
                    if (property.Value.TryGetInt32(out var id))
                        tokens[property.Name] = id;
                }
            }

            var tokenizerJson = ReadJsonObject(Path.Combine(modelFolder, "tokenizer.json"));
            if (tokenizerJson is { ValueKind: JsonValueKind.Object } &&
                tokenizerJson.Value.TryGetProperty("added_tokens", out var tokenizerAddedTokens) &&
                tokenizerAddedTokens.ValueKind == JsonValueKind.Array)
            {
                foreach (var token in tokenizerAddedTokens.EnumerateArray())
                {
                    if (token.ValueKind != JsonValueKind.Object ||
                        !token.TryGetProperty("content", out var content) ||
                        content.ValueKind != JsonValueKind.String ||
                        !token.TryGetProperty("id", out var idElement) ||
                        !idElement.TryGetInt32(out var id))
                        continue;

                    var value = content.GetString();
                    if (!string.IsNullOrEmpty(value))
                        tokens[value] = id;
                }
            }

            return tokens;
        }

        private static JsonElement? ReadJsonObject(string path)
        {
            if (!File.Exists(path))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }

        private static int GetMaximumSequenceLength(JsonElement? tokenizerConfig, JsonElement? modelConfig)
        {
            var configured = GetInteger(tokenizerConfig, "model_max_length")
                ?? GetInteger(tokenizerConfig, "max_len")
                ?? GetInteger(modelConfig, "max_position_embeddings")
                ?? 512;

            // Hugging Face uses very large sentinel values to mean "not specified".
            return configured is > 0 and < 1_000_000 ? configured : 512;
        }

        private static string GetToken(JsonElement? root, string propertyName, string fallback)
        {
            if (root is not { ValueKind: JsonValueKind.Object } ||
                !root.Value.TryGetProperty(propertyName, out var value))
                return fallback;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? fallback;

            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? fallback;

            return fallback;
        }

        private static string GetToken(
            JsonElement? tokenizerConfig,
            JsonElement? specialTokensMap,
            string propertyName,
            string fallback)
        {
            var configured = GetToken(tokenizerConfig, propertyName, string.Empty);
            return !string.IsNullOrEmpty(configured)
                ? configured
                : GetToken(specialTokensMap, propertyName, fallback);
        }

        private static string GetString(JsonElement? root, string propertyName)
        {
            return root is { ValueKind: JsonValueKind.Object } &&
                   root.Value.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool GetBoolean(JsonElement? root, string propertyName, bool fallback)
        {
            return GetNullableBoolean(root, propertyName) ?? fallback;
        }

        private static bool? GetNullableBoolean(JsonElement? root, string propertyName)
        {
            return root is { ValueKind: JsonValueKind.Object } &&
                   root.Value.TryGetProperty(propertyName, out var value) &&
                   (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : null;
        }

        private static int? GetInteger(JsonElement? root, string propertyName)
        {
            return root is { ValueKind: JsonValueKind.Object } &&
                   root.Value.TryGetProperty(propertyName, out var value) &&
                   value.TryGetInt32(out var result)
                ? result
                : null;
        }

        private static void AddSpecialToken(
            IDictionary<string, int> specialTokens,
            IReadOnlyDictionary<string, int> vocabulary,
            string token)
        {
            if (vocabulary.TryGetValue(token, out var id))
                specialTokens[token] = id;
        }

        private static int GetRequiredTokenId(IReadOnlyDictionary<string, int> vocabulary, string token)
        {
            return vocabulary.TryGetValue(token, out var id)
                ? id
                : throw new InvalidDataException($"Required tokenizer token '{token}' is missing from vocab.json.");
        }

        private static int GetRequiredTokenId(
            SentencePieceTokenizer tokenizer,
            IReadOnlyDictionary<string, int> addedTokens,
            string token)
        {
            if (addedTokens.TryGetValue(token, out var addedTokenId))
                return addedTokenId;

            if (tokenizer.Vocabulary.TryGetValue(token, out var vocabularyTokenId))
                return vocabularyTokenId;

            throw new InvalidDataException(
                $"Required tokenizer token '{token}' is missing from the SentencePiece vocabulary and added tokens.");
        }
    }

    internal enum OnnxQASequenceFormat
    {
        Bert,
        Roberta
    }

    internal sealed record OnnxQAEncodedWindow(
        long[] InputIds,
        long[] AttentionMask,
        long[] TokenTypeIds,
        int ContextStartIndex,
        int ContextTokenOffset,
        int ContextTokenCount);
}
