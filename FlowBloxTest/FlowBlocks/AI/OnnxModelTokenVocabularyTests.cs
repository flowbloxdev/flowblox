using FlowBlox.Core.Models.FlowBlocks.AI.OnnxQA;
using FlowBlox.Core.Util.Knowledge;
using Microsoft.ML.Tokenizers;

namespace FlowBloxTest.FlowBlocks.AI;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class OnnxModelTokenVocabularyTests
{
    [TestMethod]
    public void EmbeddingUsesModelUnknownIdForUnknownSentencePieceText()
    {
        using var fixture = new SentencePieceModelFixture();
        var tokenizer = new OnnxEmbeddingTokenizer(fixture.DirectoryPath);
        var tokens = tokenizer.Encode("aa\uF0FBbb");
        Assert.AreEqual(0L, tokens[0]);
        Assert.AreEqual(2L, tokens[^1]);
        Assert.Contains(3L, tokens);
        Assert.Contains(5L, tokens);
        Assert.Contains(6L, tokens);
    }

    [TestMethod]
    public void QaWindowUsesModelIdsAndPreservesUnknownCharacterOffsets()
    {
        using var fixture = new SentencePieceModelFixture();
        var tokenizer = OnnxQAModelTokenizer.Load(fixture.DirectoryPath);
        const string context = "aa\uF0FBbb";
        var question = tokenizer.EncodeTokens("a");
        var tokens = tokenizer.EncodeTokens(context);
        var unknown = tokens.Single(token => token.Value.Contains('\uF0FB'));
        Assert.AreEqual(3, unknown.Id);
        Assert.AreEqual("\uF0FB", context[unknown.Offset]);
        var window = tokenizer.BuildWindow(question, tokens, 0, tokens.Count);
        CollectionAssert.AreEqual(tokens.Select(token => (long)token.Id).ToArray(),
            window.InputIds.Skip(window.ContextStartIndex).Take(tokens.Count).ToArray());
    }

    [TestMethod]
    public void AddedSpecialTokenSharingNativeUnknownIdKeepsItsModelId()
    {
        using var fixture = new SentencePieceModelFixture();
        var tokenizer = new OnnxEmbeddingTokenizer(fixture.DirectoryPath);
        var ids = tokenizer.Encode("<s>a");
        Assert.IsFalse(ids.Contains(3L));
        Assert.IsTrue(ids.Skip(1).Contains(0L));
    }

    [TestMethod]
    public void MismatchedKnownTokenDoesNotSilentlyBecomeUnknown()
    {
        var mapping = new OnnxModelTokenVocabulary(new Dictionary<string, int> { ["<unk>"] = 3 }, 0, "<unk>");
        Assert.ThrowsExactly<InvalidDataException>(() => mapping.GetTokenId(new EncodedToken(7, "known", new Range(0, 5))));
    }
}
