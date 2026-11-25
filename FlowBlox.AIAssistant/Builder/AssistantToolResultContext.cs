using System.Text;
using FlowBlox.Core.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Builder;

internal static class AssistantToolResultContext
{
    public static IReadOnlyList<string> GetOriginalResults(AssistantMessagePair pair)
    {
        var results = ParseResults(pair.ToolApiResponse);
        return results.Count == 0
            ? [pair.ToolApiResponse]
            : results.Select(result => result.ToString(Formatting.None)).ToArray();
    }

    public static string GetCompleteContextMessage(AssistantSessionMessage message)
    {
        var pair = message as AssistantMessagePair;
        if (pair == null || pair.CompactedToolResults.Count == 0)
            return message.CompleteMessage;

        return "Assistant request:\n" + pair.AssistantRequest.Trim() +
            "\n\nTool API response:\n" + GetResponse(pair).Trim();
    }

    public static string GetResponse(AssistantMessagePair pair)
    {
        if (pair.CompactedToolResults.Count == 0)
            return pair.ToolApiResponse;

        var resultIndex = 0;
        var response = new StringBuilder();
        using var reader = new StringReader(pair.ToolApiResponse);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var item = TryParseItem(line);
            if (item != null)
            {
                if (!IsProtected(pair, resultIndex) && pair.CompactedToolResults.TryGetValue(resultIndex, out var compacted))
                {
                    var originalResult = item["response"] ?? item;
                    var replacement = WrapCompactedResult(compacted, originalResult.ToString(Formatting.None));
                    if (item["response"] != null)
                        item["response"] = replacement;
                    else
                        item = replacement;

                    line = item.ToString(Formatting.None);
                }

                resultIndex++;
            }

            response.AppendLine(line);
        }

        if (resultIndex == 0 && !IsProtected(pair, 0) && pair.CompactedToolResults.TryGetValue(0, out var fallback))
            return WrapCompactedResult(fallback, pair.ToolApiResponse).ToString(Formatting.None);

        return response.ToString().TrimEnd();
    }

    public static JObject WrapCompactedResult(string content, string? originalResult = null)
    {
        var result = new JObject
        {
            ["compacted"] = true,
            ["summary"] = content
        };
        var original = originalResult == null ? null : TryParseItem(originalResult);
        foreach (var property in original?.Properties() ?? [])
        {
            // Keep structured status/errors/logs verbatim even if the model omits them from its prose.
            if (property.Name.Equals("Ok", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Success", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("IsError", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Log", StringComparison.OrdinalIgnoreCase))
            {
                result[property.Name] = property.Value.DeepClone();
            }
        }

        return result;
    }

    public static List<JObject> ParseResults(string response)
    {
        var results = new List<JObject>();
        using var reader = new StringReader(response ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var item = TryParseItem(line);
            if (item == null)
                continue;

            if (item["response"] is JToken result)
                results.Add(result as JObject ?? new JObject { ["value"] = result });
            else
                results.Add(item);
        }

        return results;
    }

    public static bool IsProtected(AssistantMessagePair pair, int resultIndex)
    {
        return pair.RetainedContents.Any(content => content.ResultIndex < 0 || content.ResultIndex == resultIndex);
    }

    private static JObject? TryParseItem(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            return null;

        try
        {
            return JObject.Parse(trimmed);
        }
        catch (JsonException ex)
        {
            FlowBloxLogManager.Instance.GetLogger().Error(
                "Failed to parse a JSON line from a persisted Tool API response while rebuilding AI Assistant chat history.",
                ex);
            return null;
        }
    }
}
