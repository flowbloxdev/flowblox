using System.Collections.Frozen;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Provider.Placeholders;

/// <summary>Provides the built-in placeholder source IDs independently of the field registry.</summary>
public static class FlowBloxSourceIds
{
    private static readonly FrozenSet<string> SourceIds =
        Enum.GetNames<FlowBloxSourceId>().ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns all reserved source IDs using case-insensitive membership checks.</summary>
    public static IReadOnlySet<string> GetAllSourceIds() => SourceIds;

    /// <summary>Formats a placeholder using its canonical source ID and field key.</summary>
    public static string GetPlaceholder(FlowBloxSourceId sourceId, string key) => $"${sourceId}::{key}";
}
