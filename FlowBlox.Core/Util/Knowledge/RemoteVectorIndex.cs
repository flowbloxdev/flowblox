using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Components.Knowledge;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Core.Util.Knowledge;

internal sealed class RemoteVectorIndex : IDisposable
{
    private readonly VectorStoreBackend _backend;
    private readonly string _endpoint;
    private readonly string _collection;
    private readonly HttpClient _client;

    internal RemoteVectorIndex(VectorStoreBackend backend, string endpoint, string apiKey, string collection)
    {
        _backend = backend;
        _endpoint = endpoint.TrimEnd('/');
        _collection = collection;
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(60),
            MaxResponseContentBufferSize = 64 * 1024 * 1024
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var header = backend == VectorStoreBackend.Chroma ? "X-Chroma-Token" : "Api-Key";
            _client.DefaultRequestHeaders.Add(header, apiKey);
        }

        if (backend == VectorStoreBackend.Pinecone)
            _client.DefaultRequestHeaders.Add("X-Pinecone-Api-Version", "2025-10");
    }

    internal async Task ReplaceSourceAsync(string sourceId, string profile, string revision, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        var path = await CollectionPathAsync(vectors.FirstOrDefault()?.Length ?? 0, cancellationToken).ConfigureAwait(false);
        if (path == null)
            return;

        var profileId = ProfileId(profile);
        object filter = _backend == VectorStoreBackend.Qdrant
            ? new { filter = new { must = new[] { new { key = "sourceId", match = new { value = sourceId } } } } }
            : _backend == VectorStoreBackend.Pinecone
                ? new { @namespace = _collection, filter = new JObject { ["sourceId"] = new JObject { ["$eq"] = sourceId } } }
                : new { where = new JObject { ["sourceId"] = new JObject { ["$eq"] = sourceId } } };
        var deletePath = _backend == VectorStoreBackend.Qdrant ? path + "/points/delete?wait=true"
            : _backend == VectorStoreBackend.Pinecone ? "/vectors/delete" : path + "/delete";
        await SendAsync(HttpMethod.Post, deletePath, filter, cancellationToken).ConfigureAwait(false);

        for (var offset = 0; offset < vectors.Count; offset += 64)
        {
            var indices = Enumerable.Range(offset, Math.Min(64, vectors.Count - offset)).ToArray();
            var metadata = indices.Select(index => new
            {
                sourceId,
                profile = profileId,
                revision,
                chunkIndex = index
            }).ToArray();
            var ids = indices.Select(index => PointId(sourceId, profileId, index)).ToArray();
            object body;
            string upsertPath;
            if (_backend == VectorStoreBackend.Qdrant)
            {
                body = new
                {
                    points = indices.Select((index, position) => new
                    {
                        id = ids[position],
                        vector = vectors[index],
                        payload = metadata[position]
                    }).ToArray()
                };
                upsertPath = path + "/points?wait=true";
            }
            else if (_backend == VectorStoreBackend.Pinecone)
            {
                body = new
                {
                    @namespace = _collection,
                    vectors = indices.Select((index, position) => new
                    {
                        id = ids[position],
                        values = vectors[index],
                        metadata = metadata[position]
                    }).ToArray()
                };
                upsertPath = "/vectors/upsert";
            }
            else
            {
                body = new
                {
                    ids,
                    embeddings = indices.Select(index => vectors[index]).ToArray(),
                    metadatas = metadata
                };
                upsertPath = path + "/upsert";
            }

            await SendAsync(_backend == VectorStoreBackend.Qdrant ? HttpMethod.Put : HttpMethod.Post, upsertPath, body, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<IReadOnlyList<VectorSearchMatch>> SearchAsync(float[] query, string profile, int topK, ISet<string>? sources, CancellationToken cancellationToken)
    {
        if (sources?.Count == 0)
            return [];

        var path = await CollectionPathAsync(query.Length, cancellationToken).ConfigureAwait(false);
        if (path == null)
            return [];

        var profileId = ProfileId(profile);
        object body;
        string queryPath;
        if (_backend == VectorStoreBackend.Qdrant)
        {
            var must = new JArray(new JObject { ["key"] = "profile", ["match"] = new JObject { ["value"] = profileId } });
            if (sources != null)
                must.Add(new JObject { ["key"] = "sourceId", ["match"] = new JObject { ["any"] = new JArray(sources) } });

            body = new JObject
            {
                ["query"] = JArray.FromObject(query),
                ["limit"] = topK,
                ["with_payload"] = true,
                ["filter"] = new JObject { ["must"] = must }
            };
            queryPath = path + "/points/query";
        }
        else
        {
            var clauses = new JArray(new JObject { ["profile"] = new JObject { ["$eq"] = profileId } });
            if (sources != null)
                clauses.Add(new JObject { ["sourceId"] = new JObject { ["$in"] = new JArray(sources) } });

            var filter = clauses.Count == 1 ? clauses[0] : new JObject { ["$and"] = clauses };
            if (_backend == VectorStoreBackend.Pinecone)
            {
                body = new { @namespace = _collection, vector = query, topK, includeMetadata = true, filter };
                queryPath = "/query";
            }
            else
            {
                body = new { query_embeddings = new[] { query }, n_results = topK, include = new[] { "metadatas", "distances" }, where = filter };
                queryPath = path + "/query";
            }
        }

        var response = await SendAsync(HttpMethod.Post, queryPath, body, cancellationToken).ConfigureAwait(false);
        var matches = new List<VectorSearchMatch>();
        if (_backend == VectorStoreBackend.Chroma)
        {
            var metadata = response["metadatas"]?[0] as JArray ?? [];
            var distances = response["distances"]?[0] as JArray ?? [];
            if (metadata.Count != distances.Count)
                throw new InvalidDataException("Chroma returned inconsistent metadata and distance arrays.");

            for (var index = 0; index < metadata.Count; index++)
                matches.Add(ReadMatch(metadata[index], 1 - distances[index].Value<double>()));
        }
        else
        {
            var points = _backend == VectorStoreBackend.Qdrant ? response.SelectToken("result.points") : response["matches"];
            foreach (var point in points ?? new JArray())
                matches.Add(ReadMatch(point[_backend == VectorStoreBackend.Qdrant ? "payload" : "metadata"]!, point.Value<double>("score")));
        }

        return matches;
    }

    private async Task<string?> CollectionPathAsync(int dimensions, CancellationToken cancellationToken)
    {
        if (_backend == VectorStoreBackend.Pinecone)
            return string.Empty;

        var path = "/collections/" + Uri.EscapeDataString(_collection);
        var json = await SendAsync(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        if (json == null)
        {
            if (_backend == VectorStoreBackend.Qdrant)
            {
                if (dimensions == 0)
                    return null;

                await SendAsync(HttpMethod.Put, path, new { vectors = new { size = dimensions, distance = "Cosine" } }, cancellationToken).ConfigureAwait(false);
                await EnsureQdrantIndexesAsync(path, null, cancellationToken).ConfigureAwait(false);
                return path;
            }

            json = await SendAsync(HttpMethod.Post, "/collections", new
            {
                name = _collection,
                get_or_create = true,
                configuration = new { hnsw = new { space = "cosine" } }
            }, cancellationToken).ConfigureAwait(false);
        }

        if (_backend == VectorStoreBackend.Qdrant)
        {
            var config = json.SelectToken("result.config.params.vectors");
            if (config?.Value<string>("distance") != "Cosine" || dimensions > 0 && config.Value<int>("size") != dimensions)
                throw new InvalidDataException("Qdrant requires an unnamed Cosine vector collection with matching embedding dimensions.");

            await EnsureQdrantIndexesAsync(path, json.SelectToken("result.payload_schema"), cancellationToken).ConfigureAwait(false);
            return path;
        }

        var space = json.SelectToken("configuration_json.hnsw.space")?.Value<string>()
            ?? json.SelectToken("configuration.hnsw.space")?.Value<string>();
        if (space != "cosine")
            throw new InvalidDataException("Chroma requires a collection configured with cosine distance.");

        return "/collections/" + Uri.EscapeDataString(json.Value<string>("id")!);
    }

    private async Task EnsureQdrantIndexesAsync(string path, JToken? schema, CancellationToken cancellationToken)
    {
        foreach (var field in new[] { "sourceId", "profile" })
        {
            if (schema?[field] != null)
                continue;

            await SendAsync(HttpMethod.Put, path + "/index?wait=true", new
            {
                field_name = field,
                field_schema = "keyword"
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JToken?> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, _endpoint + path);
        if (body != null)
        {
            // Newtonsoft tokens must be serialized by Newtonsoft, rather than System.Text.Json.
            request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (new Uri(_endpoint).IsLoopback
            && exception.InnerException is SocketException socketException
            && socketException.SocketErrorCode == SocketError.ConnectionRefused)
        {
            throw new ExecutionEnvironmentPrerequisiteException(_backend.ToString(), typeof(VectorStore), _endpoint, exception);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(content) ? new JObject() : JToken.Parse(content);
        }
    }

    private static VectorSearchMatch ReadMatch(JToken metadata, double score) => new()
    {
        SourceId = metadata.Value<string>("sourceId") ?? throw new InvalidDataException("Vector search returned no source ID."),
        ChunkIndex = metadata.Value<int>("chunkIndex"),
        Revision = metadata.Value<string>("revision") ?? string.Empty,
        Score = score
    };

    private static string ProfileId(string profile) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
    private static string PointId(string sourceId, string profileId, int index) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId + "\0" + profileId + "\0" + index)).AsSpan(0, 16)).ToString();

    public void Dispose() => _client.Dispose();
}
