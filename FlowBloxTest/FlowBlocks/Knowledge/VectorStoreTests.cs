using FlowBlox.Core.Models.Components.Knowledge;

namespace FlowBloxTest.FlowBlocks.Knowledge;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class VectorStoreTests
{
    private string _directory = null!;
    private VectorStore _store = null!;
    private StubEmbeddingProvider _embeddings = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxVectors", Guid.NewGuid().ToString("N"));
        _store = new VectorStore
        {
            DatabasePath = Path.Combine(_directory, "nested", "knowledge.sqlite"),
            ChunkSize = 64,
            ChunkOverlap = 8
        };
        _embeddings = new StubEmbeddingProvider { ApiKey = "test-key", Model = "test-model" };
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task UnchangedSourceReusesPersistedEmbeddingsAcrossInstances()
    {
        Assert.IsTrue(await _store.UpsertAsync("source", "Title", "TBS production", _embeddings, default));
        var reopened = new VectorStore { DatabasePath = _store.DatabasePath, ChunkSize = 64, ChunkOverlap = 8 };
        Assert.IsFalse(await reopened.UpsertAsync("source", "Title", "TBS production", _embeddings, default));
        Assert.AreEqual(1, _embeddings.Calls.Count);
        Assert.AreEqual("TBS production", reopened.Search([1, 0], _embeddings, 1).Rows[0]["Text"]);
    }

    [TestMethod]
    public async Task ShortenedSourceReplacesOldChunksWithoutDuplicates()
    {
        await _store.UpsertAsync("source", "Title", new string('x', 180), _embeddings, default);
        await _store.UpsertAsync("source", "Title", "TBS new server", _embeddings, default);
        var matches = _store.Search([1, 0], _embeddings, 100);
        Assert.AreEqual(1, matches.Rows.Count);
        Assert.AreEqual("TBS new server", matches.Rows[0]["Text"]);
    }

    [TestMethod]
    public async Task FailedEmbeddingKeepsPreviousSourceRevision()
    {
        await _store.UpsertAsync("source", "Title", "TBS original", _embeddings, default);
        _embeddings.FailNext = true;
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => _store.UpsertAsync("source", "Title", "replacement", _embeddings, default));
        Assert.AreEqual("TBS original", _store.Search([1, 0], _embeddings, 1).Rows[0]["Text"]);
    }

    [TestMethod]
    public async Task ChangedEmbeddingModelReindexesUnchangedContent()
    {
        await _store.UpsertAsync("source", "Title", "TBS server", _embeddings, default);
        _embeddings.Model = "new-model";
        Assert.AreEqual(0, _store.Search([1, 0], _embeddings, 1).Rows.Count);
        Assert.IsTrue(await _store.UpsertAsync("source", "Title", "TBS server", _embeddings, default));
        Assert.AreEqual(2, _embeddings.Calls.Count);
    }

    [TestMethod]
    public async Task SearchRanksSemanticallyAndExcludesSourcesRemovedFromCurrentList()
    {
        await _store.UpsertAsync("old", "Old", "TBS stale server", _embeddings, default);
        await _store.UpsertAsync("current", "Current", "TBS prod server", _embeddings, default);
        await _store.UpsertAsync("other", "Other", "Weather forecast", _embeddings, default);
        var matches = _store.Search([1, 0], _embeddings, 1, new HashSet<string> { "current", "other" });
        Assert.AreEqual(1, matches.Rows.Count);
        Assert.AreEqual("current", matches.Rows[0]["SourceId"]);
        Assert.AreEqual(1d, (double)matches.Rows[0]["Score"], 0.00001);
    }

    [TestMethod]
    public async Task EmptyRevisionRemovesPreviouslyIndexedChunks()
    {
        await _store.UpsertAsync("source", "Title", "TBS server", _embeddings, default);
        await _store.UpsertAsync("source", "Title", string.Empty, _embeddings, default);
        Assert.AreEqual(0, _store.Search([1, 0], _embeddings, 1).Rows.Count);
    }
}
