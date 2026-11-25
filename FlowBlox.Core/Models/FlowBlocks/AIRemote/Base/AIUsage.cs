using Newtonsoft.Json.Linq;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

/// <summary>
/// Provider-reported usage. Null categories indicate unavailable measurements.
/// </summary>
public sealed class AIUsage
{
    public long? TotalInputTokens { get; set; }
    public long? UncachedInputTokens { get; set; }
    public long? CachedInputTokens { get; set; }
    public long? CacheWriteInputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? TotalTokens => TotalInputTokens + OutputTokens;

    public static AIUsage FromResponse(JObject response, bool anthropic)
    {
        var usage = response["usage"] as JObject ?? response["choices"]?.First?["usage"] as JObject;
        if (usage == null)
            return new AIUsage();

        if (anthropic)
        {
            var regular = (long?)usage["input_tokens"];
            var read = (long?)usage["cache_read_input_tokens"] ?? 0;
            var write = (long?)usage["cache_creation_input_tokens"] ?? 0;
            return new AIUsage
            {
                TotalInputTokens = regular + read + write,
                UncachedInputTokens = regular,
                CachedInputTokens = read,
                CacheWriteInputTokens = write,
                OutputTokens = (long?)usage["output_tokens"]
            };
        }

        var total = (long?)usage["prompt_tokens"];
        var cached = (long?)usage["prompt_cache_hit_tokens"] ??
            (long?)usage["prompt_tokens_details"]?["cached_tokens"] ?? (long?)usage["cached_tokens"];
        return new AIUsage
        {
            TotalInputTokens = total,
            CachedInputTokens = cached,
            UncachedInputTokens = (long?)usage["prompt_cache_miss_tokens"] ?? total - cached,
            CacheWriteInputTokens = 0,
            OutputTokens = (long?)usage["completion_tokens"]
        };
    }
}
