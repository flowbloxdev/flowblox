using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util;

/// <summary>
/// Selects complete snapshot subtrees and reports their original paths without shifting array indices.
/// </summary>
internal static class SnapshotJsonFilter
{
    public static JArray Select(JToken snapshot, JToken jsonFilter)
    {
        if (jsonFilter is not JArray paths || paths.Count == 0)
            throw new ArgumentException("jsonFilter must be a non-empty JSON array of JPath strings. Omit it for a full snapshot.");

        var selected = new List<JToken>();
        var unique = new HashSet<JToken>(ReferenceEqualityComparer.Instance);
        foreach (var entry in paths)
        {
            if (entry.Type != JTokenType.String || string.IsNullOrWhiteSpace(entry.Value<string>()))
                throw new ArgumentException("Every jsonFilter entry must be a non-empty JPath string rooted at '$'.");

            var path = entry.Value<string>()!.Trim();
            if (!path.StartsWith('$') || path.Length > 1 && path[1] != '.' && path[1] != '[')
                throw new ArgumentException($"Invalid JPath '{path}'. Use snapshot-rooted JPaths such as '$.MappingEntries', not slash update paths.");

            List<JToken> matches;
            try
            {
                matches = snapshot.SelectTokens(path).ToList();
            }
            catch (JsonException exception)
            {
                throw new ArgumentException($"Invalid JPath '{path}': {exception.Message}", exception);
            }

            if (matches.Count == 0)
                throw new ArgumentException($"JPath '{path}' matched no snapshot value. Check the exact property name and compact snapshot shape.");

            foreach (var match in matches)
            {
                if (unique.Add(match))
                    selected.Add(match);
            }
        }

        var result = new JArray();
        foreach (var token in selected)
        {
            // An explicitly selected parent already includes its complete descendants.
            if (token.Ancestors().Any(unique.Contains))
                continue;

            var path = token.Path;
            result.Add(new JObject
            {
                ["path"] = string.IsNullOrEmpty(path) ? "$" : path.StartsWith('[') ? "$" + path : "$." + path,
                ["value"] = token.DeepClone()
            });
        }

        return result;
    }
}
