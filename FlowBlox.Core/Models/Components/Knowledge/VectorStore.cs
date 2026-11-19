using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Knowledge;
using FlowBlox.Core.Util.Resources;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace FlowBlox.Core.Models.Components.Knowledge;

[Display(Name = "VectorStore_DisplayName", Description = "VectorStore_Description", ResourceType = typeof(FlowBloxTexts))]
public class VectorStore : ManagedObject, IValidatableObject
{
    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_outline, 16, SKColors.MediumPurple);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_outline, 32, SKColors.MediumPurple);

    [Display(Name = "VectorStore_Backend", ResourceType = typeof(FlowBloxTexts), Order = -1)]
    public VectorStoreBackend Backend { get; set; }

    [ConditionallyRequired]
    [ActivationCondition(ActivationMethod = nameof(IsRemote))]
    [Display(Name = "VectorStore_Endpoint", Description = "VectorStore_Endpoint_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
    public string Endpoint { get; set; }

    [ActivationCondition(ActivationMethod = nameof(IsRemote))]
    [Display(Name = "AIProvider_ApiKey", ResourceType = typeof(FlowBloxTexts), Order = 5)]
    public string ApiKey { get; set; }

    private bool IsRemote() => this.Resolve(store => store.Backend) != VectorStoreBackend.EmbeddedSqlite;

    [Required]
    [Display(Name = "VectorStore_DatabasePath", Description = "VectorStore_DatabasePath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    public string DatabasePath { get; set; }

    [Required]
    [Display(Name = "VectorStore_Collection", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    public string Collection { get; set; } = "knowledge";

    [Range(64, 8000)]
    [Display(Name = "VectorStore_ChunkSize", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    public int ChunkSize { get; set; } = 2000;

    [Range(0, 7999)]
    [Display(Name = "VectorStore_ChunkOverlap", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    public int ChunkOverlap { get; set; } = 200;

    public override List<string> GetDisplayableProperties() => [nameof(Name), nameof(Backend), nameof(DatabasePath), nameof(Collection), nameof(Endpoint)];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ChunkOverlap >= ChunkSize && !PropertySelectedFieldMap.ContainsKey(nameof(ChunkOverlap)) && !PropertySelectedFieldMap.ContainsKey(nameof(ChunkSize)))
            yield return new ValidationResult(FlowBloxTexts.VectorStore_InvalidOverlap, [nameof(ChunkOverlap)]);
    }

    public async Task<bool> UpsertAsync(
        string sourceId,
        string title,
        string content,
        TextEmbeddingProviderBase embeddings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var collection = this.Resolve(store => store.Collection, required: true);
        var size = this.Resolve(store => store.ChunkSize);
        var overlap = this.Resolve(store => store.ChunkOverlap);
        var profile = GetProfile(embeddings, size, overlap);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((title ?? string.Empty) + "\0" + content)));
        using var connection = Open();
        using (var existing = connection.CreateCommand())
        {
            existing.CommandText = "SELECT content_hash FROM knowledge_sources WHERE collection=$collection AND source_id=$source AND profile=$profile";
            existing.Parameters.AddWithValue("$collection", collection);
            existing.Parameters.AddWithValue("$source", sourceId);
            existing.Parameters.AddWithValue("$profile", profile);
            if (existing.ExecuteScalar() as string == hash)
                return false;
        }

        var chunks = embeddings.SplitText(content ?? string.Empty, size, overlap);
        var vectors = await embeddings.EmbedAsync(chunks, cancellationToken).ConfigureAwait(false);
        if (vectors.Count != chunks.Count)
            throw new InvalidDataException("The embedding provider returned an incorrect vector count.");

        foreach (var vector in vectors)
        {
            VectorMath.Validate(vector);
            if (vector.Length != vectors[0].Length)
                throw new InvalidDataException("Embedding dimensions differ between document chunks.");
        }

        if (IsRemote())
        {
            using var remote = CreateRemoteIndex();
            await remote.ReplaceSourceAsync(sourceId, profile, hash, vectors, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM knowledge_chunks WHERE collection=$collection AND source_id=$source";
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$source", sourceId);
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO knowledge_sources(collection, source_id, content_hash, profile)
            VALUES($collection, $source, $hash, $profile)
            ON CONFLICT(collection, source_id) DO UPDATE SET content_hash=excluded.content_hash, profile=excluded.profile
            """;
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$profile", profile);
        command.ExecuteNonQuery();
        for (var index = 0; index < chunks.Count; index++)
        {
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO knowledge_chunks(collection, source_id, chunk_index, title, content, vector, profile)
                VALUES($collection, $source, $index, $title, $content, $vector, $profile)
                """;
            command.Parameters.AddWithValue("$collection", collection);
            command.Parameters.AddWithValue("$source", sourceId);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$title", title ?? string.Empty);
            command.Parameters.AddWithValue("$content", chunks[index]);
            command.Parameters.AddWithValue("$vector", JsonSerializer.Serialize(vectors[index]));
            command.Parameters.AddWithValue("$profile", profile);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    public DataTable Search(float[] query, TextEmbeddingProviderBase embeddings, int topK, ISet<string>? activeSources = null, CancellationToken cancellationToken = default)
    {
        VectorMath.Validate(query);
        var collection = this.Resolve(store => store.Collection, required: true);
        var profile = GetProfile(embeddings, this.Resolve(store => store.ChunkSize), this.Resolve(store => store.ChunkOverlap));
        IReadOnlyList<VectorSearchMatch>? remoteMatches = null;
        if (IsRemote())
        {
            using var remote = CreateRemoteIndex();
            remoteMatches = remote.SearchAsync(query, profile, topK, activeSources, cancellationToken).GetAwaiter().GetResult();
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.source_id, c.title, c.chunk_index, c.content, c.vector, s.content_hash
            FROM knowledge_chunks c
            JOIN knowledge_sources s ON s.collection=c.collection AND s.source_id=c.source_id
            WHERE c.collection=$collection AND c.profile=$profile
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$profile", profile);
        using var reader = command.ExecuteReader();
        var result = CreateResultTable();
        var best = new PriorityQueue<object[], double>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceId = reader.GetString(0);
            if (activeSources != null && !activeSources.Contains(sourceId))
                continue;

            var vector = JsonSerializer.Deserialize<float[]>(reader.GetString(4))!;
            if (remoteMatches != null && !remoteMatches.Any(match => match.SourceId == sourceId
                    && match.ChunkIndex == reader.GetInt32(2) && match.Revision == reader.GetString(5)))
                continue;

            // Recompute the score from the current local revision; stale remote points never supply answer content.
            var score = VectorMath.Cosine(query, vector);
            best.Enqueue([sourceId, reader.GetString(1), reader.GetInt32(2), reader.GetString(3), score], score);
            if (best.Count > topK)
                best.Dequeue();
        }

        foreach (var row in best.UnorderedItems.OrderByDescending(item => item.Priority).ThenBy(item => (string)item.Element[0], StringComparer.Ordinal))
            result.Rows.Add(row.Element);

        return result;
    }

    public DataTable GetSources(TextEmbeddingProviderBase embeddings)
    {
        var collection = this.Resolve(store => store.Collection, required: true);
        var profile = GetProfile(embeddings, this.Resolve(store => store.ChunkSize), this.Resolve(store => store.ChunkOverlap));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_id AS SourceId, content_hash AS ContentHash
            FROM knowledge_sources
            WHERE collection=$collection AND profile=$profile
              AND EXISTS (SELECT 1 FROM knowledge_chunks c
                          WHERE c.collection=knowledge_sources.collection
                            AND c.source_id=knowledge_sources.source_id AND c.profile=$profile)
            ORDER BY source_id
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$profile", profile);
        using var reader = command.ExecuteReader();
        var result = new DataTable();
        result.Load(reader);
        return result;
    }

    private SqliteConnection Open()
    {
        var path = Path.GetFullPath(this.Resolve(store => store.DatabasePath, required: true));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SqliteConnection? connection = null;
        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=30000;
                CREATE TABLE IF NOT EXISTS knowledge_sources(
                    collection TEXT NOT NULL, source_id TEXT NOT NULL, content_hash TEXT NOT NULL, profile TEXT NOT NULL,
                    PRIMARY KEY(collection, source_id));
                CREATE TABLE IF NOT EXISTS knowledge_chunks(
                    collection TEXT NOT NULL, source_id TEXT NOT NULL, chunk_index INTEGER NOT NULL,
                    title TEXT NOT NULL, content TEXT NOT NULL, vector TEXT NOT NULL, profile TEXT NOT NULL,
                    PRIMARY KEY(collection, source_id, chunk_index));
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is TypeInitializationException && exception.GetBaseException() is DllNotFoundException)
        {
            connection?.Dispose();
            throw new ExecutionEnvironmentPrerequisiteException("SQLite runtime", typeof(VectorStore), path, exception);
        }
        catch
        {
            connection?.Dispose();
            throw;
        }
    }

    private string GetProfile(TextEmbeddingProviderBase embeddings, int size, int overlap) =>
        $"{embeddings.GetProfile()}|chunks-v1|{size}|{overlap}|{this.Resolve(store => store.Backend)}|{this.Resolve(store => store.Endpoint)}";

    private RemoteVectorIndex CreateRemoteIndex() => new(
        this.Resolve(store => store.Backend),
        this.Resolve(store => store.Endpoint, required: true),
        this.Resolve(store => store.ApiKey),
        this.Resolve(store => store.Collection, required: true));

    private static DataTable CreateResultTable()
    {
        var table = new DataTable();
        table.Columns.Add("SourceId", typeof(string));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("ChunkIndex", typeof(int));
        table.Columns.Add("Text", typeof(string));
        table.Columns.Add("Score", typeof(double));
        return table;
    }
}
