using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace FlowBlox.Core.Models.FlowBlocks.Additions;

/// <summary>
/// Shares immutable line sets by content; weak string keys avoid hashing the same instance repeatedly.
/// </summary>
internal sealed class LineSetCache : IDisposable
{
    private static readonly TimeSpan IdleExpiration = TimeSpan.FromMinutes(2);
    private readonly ConditionalWeakTable<string, string> _contentHashes = new();
    private readonly Lock _sync = new();
    private readonly MemoryCache _cache;

    internal static LineSetCache Shared { get; } = new(new MemoryCacheOptions());

    internal LineSetCache(MemoryCacheOptions options)
    {
        _cache = new MemoryCache(options);
    }

    internal FrozenSet<string> GetLines(string text)
    {
        var hash = _contentHashes.GetValue(text, CalculateHash);
        Lazy<FrozenSet<string>> lines;
        lock (_sync)
        {
            if (!_cache.TryGetValue(hash, out lines))
            {
                lines = new Lazy<FrozenSet<string>>(() => CreateLines(text));
                _cache.Set(hash, lines, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = IdleExpiration
                });
            }
        }

        return lines.Value;
    }

    private static string CalculateHash(string text) =>
        Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(text.AsSpan())));

    private static FrozenSet<string> CreateLines(string text)
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            lines.Add(line);

        return lines.ToFrozenSet(StringComparer.Ordinal);
    }

    public void Dispose() => _cache.Dispose();
}
