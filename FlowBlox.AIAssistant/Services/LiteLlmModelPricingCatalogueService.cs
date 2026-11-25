using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Services;

/// <summary>
/// Resolves provider/model prices from LiteLLM, keeping a persistent last valid entry per identity.
/// A shared download and a retry delay prevent requests on every completion.
/// </summary>
public sealed class LiteLlmModelPricingCatalogueService : IModelPricingCatalogueService
{
    public const string CatalogueUrl = "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";
    private readonly HttpClient _client;
    private readonly string _directory;
    private readonly Func<TimeSpan> _refreshInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JObject? _catalogue;
    private DateTimeOffset _downloadedAt;
    private DateTimeOffset _retryAfter;
    private readonly Dictionary<string, JObject> _entries = new(StringComparer.Ordinal);

    public LiteLlmModelPricingCatalogueService() : this(
        new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        },
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowBlox", "ai_pricingcatalogues"),
        ReadRefreshInterval)
    {
    }

    public LiteLlmModelPricingCatalogueService(
        HttpClient client,
        string directory,
        Func<TimeSpan> refreshInterval)
    {
        _client = client;
        _directory = directory;
        _refreshInterval = refreshInterval;
    }

    private static TimeSpan ReadRefreshInterval()
    {
        var value = FlowBloxOptions.GetOptionInstance().GetOption("AI.PricingCatalogueRefreshHours")?.Value;
        return TimeSpan.FromHours(double.TryParse(value, out var hours) && hours >= 1 && hours <= 8760 ? hours : 72);
    }

    public async Task<ModelPricing?> GetPricingAsync(
        string provider,
        string model,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var identity = provider + ":" + model;
            var filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json";
            var path = Path.Combine(_directory, filename);
            if (!_entries.TryGetValue(identity, out var entry))
            {
                entry = await ReadEntryAsync(path, cancellationToken).ConfigureAwait(false);
                _entries[identity] = entry;
            }

            var now = DateTimeOffset.UtcNow;
            var fetched = DateTimeOffset.TryParse(entry["fetchedAt"]?.ToString(), out var timestamp)
                ? timestamp
                : DateTimeOffset.MinValue;
            if (now - fetched >= _refreshInterval() && now >= _retryAfter)
            {
                try
                {
                    if (_catalogue == null || now - _downloadedAt >= _refreshInterval())
                    {
                        var json = await _client.GetStringAsync(CatalogueUrl, cancellationToken).ConfigureAwait(false);
                        var catalogue = JObject.Parse(json);
                        if (!catalogue.Properties().Any(x => x.Value is JObject modelEntry && modelEntry["litellm_provider"] != null))
                            throw new JsonException("Pricing catalogue contains no provider entries.");
                        _catalogue = catalogue;
                        _downloadedAt = now;
                    }

                    var match = ResolveEntry(_catalogue, provider, model);
                    entry = new JObject
                    {
                        ["provider"] = provider,
                        ["model"] = model,
                        ["fetchedAt"] = now,
                        ["pricing"] = match?.DeepClone()
                    };
                    _entries[identity] = entry;
                    Directory.CreateDirectory(_directory);
                    var temporaryPath = path + ".tmp";
                    await File.WriteAllTextAsync(temporaryPath, entry.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
                    File.Move(temporaryPath, path, true);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException or TaskCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _retryAfter = now.AddMinutes(15);
                    FlowBloxLogManager.Instance.GetLogger().Warn("Failed to refresh the AI pricing catalogue; retaining cached prices.", ex);
                }
            }

            try
            {
                return ParsePricing(entry["pricing"] as JObject);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or ArgumentException)
            {
                FlowBloxLogManager.Instance.GetLogger().Warn("Failed to parse AI model pricing; costs are unavailable.", ex);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<JObject> ReadEntryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(path))
                return JObject.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            FlowBloxLogManager.Instance.GetLogger().Warn("Failed to read the cached AI pricing entry.", ex);
        }
        return new JObject();
    }

    internal static JObject? ResolveEntry(JObject catalogue, string provider, string model)
    {
        var providerId = provider.ToLowerInvariant() switch
        {
            "kimi" or "moonshot" => "moonshot",
            "xai" => "xai",
            _ => provider.ToLowerInvariant()
        };
        var bareModel = model.StartsWith(providerId + "/", StringComparison.OrdinalIgnoreCase)
            ? model[(providerId.Length + 1)..]
            : model;
        foreach (var key in new[] { providerId + "/" + bareModel, bareModel })
        {
            var candidate = catalogue[key] as JObject;
            if (candidate == null)
                continue;
            var actualProvider = (string?)candidate["litellm_provider"];
            if (string.Equals(actualProvider, providerId, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    private static ModelPricing? ParsePricing(JObject? entry)
    {
        if (entry == null)
            return null;
        var tiers = new Dictionary<long, ModelPricingTier>();
        var unsupportedTiers = false;
        foreach (var property in entry.Properties().Where(x => x.Name.Contains("_above_", StringComparison.Ordinal)))
        {
            var match = Regex.Match(property.Name, @"^(input_cost_per_token|output_cost_per_token|cache_read_input_token_cost|cache_creation_input_token_cost)_above_(\d+)(k|m)?_tokens$");
            if (!match.Success)
            {
                if (!property.Name.EndsWith("_above_1hr", StringComparison.Ordinal))
                    unsupportedTiers = true;
                continue;
            }
            var scale = match.Groups[3].Value switch
            {
                "m" => 1000000,
                "k" => 1000,
                _ => 1
            };
            var threshold = long.Parse(match.Groups[2].Value) * scale;
            if (!tiers.TryGetValue(threshold, out var tier))
                tiers[threshold] = tier = new ModelPricingTier { AboveInputTokens = threshold };
            var price = (decimal?)property.Value;
            switch (match.Groups[1].Value)
            {
                case "input_cost_per_token":
                    tier.InputCostPerToken = price;
                    break;
                case "output_cost_per_token":
                    tier.OutputCostPerToken = price;
                    break;
                case "cache_read_input_token_cost":
                    tier.CacheReadCostPerToken = price;
                    break;
                case "cache_creation_input_token_cost":
                    tier.CacheWriteCostPerToken = price;
                    break;
            }
        }
        return new ModelPricing
        {
            InputCostPerToken = (decimal?)entry["input_cost_per_token"],
            OutputCostPerToken = (decimal?)entry["output_cost_per_token"],
            CacheReadCostPerToken = (decimal?)entry["cache_read_input_token_cost"] ?? (decimal?)entry["input_cost_per_token_cache_hit"],
            CacheWriteCostPerToken = (decimal?)entry["cache_creation_input_token_cost"],
            MaxInputTokens = (int?)entry["max_input_tokens"],
            MaxOutputTokens = (int?)entry["max_output_tokens"],
            MaxTokens = (int?)entry["max_tokens"],
            Tiers = tiers.Values.ToList(),
            HasUnsupportedTiers = unsupportedTiers
        };
    }
}
