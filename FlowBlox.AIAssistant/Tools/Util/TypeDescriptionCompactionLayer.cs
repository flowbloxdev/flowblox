using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util;

/// <summary>
/// Encodes common property metadata as positional rows while preserving all other metadata by name.
/// </summary>
internal static class TypeDescriptionCompactionLayer
{
    internal const string Description =
        "When propertyColumns is present, properties are positional rows using that shared schema in kind and additionalTypeKindsInfo. " +
        "The final extras object contains all remaining named property metadata; merge it with the columns.";

    private static readonly string[] CommonColumns =
    [
        "name",
        "type",
        "flags",
        "majorTypeDescriptor",
        "canUpdateViaParentPath",
        "displayName"
    ];

    public static JObject Compact(JObject payload)
    {
        var result = (JObject)payload.DeepClone();
        if (result.ContainsKey("propertyColumns"))
            return result;

        var compacted = false;
        if (result["kind"] is JObject kind)
            compacted = CompactKind(kind);

        foreach (var additionalKind in result["additionalTypeKindsInfo"]?.Children<JObject>() ?? [])
            compacted |= CompactKind(additionalKind);

        if (compacted)
            result["propertyColumns"] = new JArray(CommonColumns.Append("extras"));

        return result;
    }

    private static bool CompactKind(JObject kind)
    {
        if (kind["properties"] is not JArray properties || properties.Count == 0)
            return false;

        var rows = new JArray();
        foreach (var token in properties)
        {
            if (token is not JObject property)
                throw new InvalidOperationException("Type property metadata must contain named property objects before compaction.");

            var extras = (JObject)property.DeepClone();
            var row = new JArray();
            foreach (var column in CommonColumns)
            {
                var value = extras.Property(column)
                    ?? throw new InvalidOperationException($"Type property metadata is missing the '{column}' column.");
                row.Add(value.Value.DeepClone());
                value.Remove();
            }

            row.Add(extras);
            rows.Add(row);
        }

        kind["properties"] = rows;
        return true;
    }
}
