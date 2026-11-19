using System.Net;
using System.Net.Sockets;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Components.Knowledge;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.FlowBlocks.Knowledge;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class KnowledgeApiTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxKnowledgeApi", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public async Task EmbeddingResponseIsReorderedByInputIndex()
    {
        using var api = new KnowledgeHttpServer(_ => new KnowledgeHttpResponse
        {
            Body = "{\"data\":[{\"index\":1,\"embedding\":[0,1]},{\"index\":0,\"embedding\":[1,0]}]}"
        });
        var provider = new TextEmbeddingProvider { Endpoint = api.BaseUrl + "/embeddings", ApiKey = "embedding-key" };
        var vectors = await provider.EmbedAsync(["first", "second"], default);
        CollectionAssert.AreEqual(new float[] { 1, 0 }, vectors[0]);
        CollectionAssert.AreEqual(new float[] { 0, 1 }, vectors[1]);
        Assert.AreEqual("Bearer embedding-key", api.Requests.Single().Headers["Authorization"]);
        CollectionAssert.AreEqual(new[] { "first", "second" }, api.Requests.Single().Json["input"]!.Values<string>().ToArray());
    }

    [TestMethod]
    public async Task IncompleteEmbeddingResponseIsRejected()
    {
        using var api = new KnowledgeHttpServer(_ => new KnowledgeHttpResponse { Body = "{\"data\":[{\"index\":0,\"embedding\":[1,0]}]}" });
        var provider = new TextEmbeddingProvider { Endpoint = api.BaseUrl, ApiKey = "embedding-key" };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => provider.EmbedAsync(["first", "second"], default));
    }

    [TestMethod]
    [DataRow(VectorStoreBackend.Qdrant, false)]
    [DataRow(VectorStoreBackend.Pinecone, false)]
    [DataRow(VectorStoreBackend.Chroma, false)]
    [DataRow(VectorStoreBackend.Pinecone, true)]
    public async Task RemoteBackendSynchronizesAndRetrievesCurrentLocalDocumentText(VectorStoreBackend backend, bool staleRevision)
    {
        var created = false;
        JToken? storedMetadata = null;
        using var api = new KnowledgeHttpServer(request =>
        {
            if (request.Method == "GET")
                return !created ? new KnowledgeHttpResponse { Status = 404 }
                    : new KnowledgeHttpResponse { Body = CollectionResponse(backend) };

            if (request.Method == "PUT" && request.Path == "/collections/knowledge" || request.Method == "POST" && request.Path == "/collections")
            {
                created = true;
                return new KnowledgeHttpResponse { Body = CollectionResponse(backend) };
            }

            if (request.Path.Contains("/points?", StringComparison.Ordinal))
                storedMetadata = request.Json.SelectToken("points[0].payload");
            else if (request.Path.EndsWith("/upsert", StringComparison.Ordinal))
                storedMetadata = backend == VectorStoreBackend.Pinecone ? request.Json.SelectToken("vectors[0].metadata") : request.Json.SelectToken("metadatas[0]");

            if (request.Path.EndsWith("/query", StringComparison.Ordinal))
            {
                Assert.IsNotNull(storedMetadata);
                if (staleRevision)
                    storedMetadata["revision"] = "previous-revision";
                var response = backend == VectorStoreBackend.Qdrant
                    ? new JObject { ["result"] = new JObject { ["points"] = new JArray(new JObject { ["score"] = 1, ["payload"] = storedMetadata }) } }
                    : backend == VectorStoreBackend.Pinecone
                        ? new JObject { ["matches"] = new JArray(new JObject { ["score"] = 1, ["metadata"] = storedMetadata }) }
                        : new JObject { ["metadatas"] = new JArray { new JArray { storedMetadata } }, ["distances"] = new JArray { new JArray { 0 } } };
                return new KnowledgeHttpResponse { Body = response.ToString() };
            }

            return new KnowledgeHttpResponse();
        });
        var embeddings = new StubEmbeddingProvider { ApiKey = "embedding-key" };
        var store = new VectorStore { Backend = backend, Endpoint = api.BaseUrl, ApiKey = "vector-key", DatabasePath = Path.Combine(_directory, "index.sqlite") };
        await store.UpsertAsync("source", "Title", "TBS prod server", embeddings, default);
        var count = api.Requests.Count;
        Assert.IsFalse(await store.UpsertAsync("source", "Title", "TBS prod server", embeddings, default));
        Assert.AreEqual(count, api.Requests.Count);
        var result = store.Search([1, 0], embeddings, 3, new HashSet<string> { "source" });
        if (staleRevision)
            Assert.AreEqual(0, result.Rows.Count);
        else
            Assert.AreEqual("TBS prod server", result.Rows[0]["Text"]);
        Assert.IsTrue(api.Requests.All(request => request.Headers[backend == VectorStoreBackend.Chroma ? "X-Chroma-Token" : "Api-Key"] == "vector-key"));
    }

    [TestMethod]
    public async Task MissingLocalVectorServerRaisesEnvironmentPrerequisite()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var store = new VectorStore
        {
            Backend = VectorStoreBackend.Qdrant,
            Endpoint = $"http://127.0.0.1:{port}",
            DatabasePath = Path.Combine(_directory, "index.sqlite")
        };
        var embeddings = new StubEmbeddingProvider { ApiKey = "test-key" };
        await Assert.ThrowsExactlyAsync<ExecutionEnvironmentPrerequisiteException>(() => store.UpsertAsync("source", "Title", "TBS server", embeddings, default));
    }

    private static string CollectionResponse(VectorStoreBackend backend) => backend == VectorStoreBackend.Qdrant
        ? "{\"result\":{\"config\":{\"params\":{\"vectors\":{\"size\":2,\"distance\":\"Cosine\"}}}}}"
        : "{\"id\":\"collection-id\",\"configuration_json\":{\"hnsw\":{\"space\":\"cosine\"}}}";
}
