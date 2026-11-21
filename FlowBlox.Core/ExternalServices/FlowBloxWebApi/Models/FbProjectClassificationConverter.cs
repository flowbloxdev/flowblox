using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;

/// <summary>
/// Reads legacy Standard metadata while writing only the canonical classification names.
/// </summary>
public sealed class FbProjectClassificationConverter : StringEnumConverter
{
    public override object ReadJson(
        JsonReader reader,
        Type objectType,
        object existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.String
            && string.Equals(reader.Value as string, "Standard", StringComparison.OrdinalIgnoreCase))
            return FbProjectClassification.Solution;

        return base.ReadJson(reader, objectType, existingValue, serializer);
    }
}
