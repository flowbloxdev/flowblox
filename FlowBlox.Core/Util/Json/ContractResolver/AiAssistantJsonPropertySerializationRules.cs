using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.Compression;
using FlowBlox.Core.Util.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Collections;
using System.Reflection;
using FlowBlox.Core.Util.Json.ValueProvider;

namespace FlowBlox.Core.Util.Json.ContractResolver
{
    internal static class AiAssistantJsonPropertySerializationRules
    {
        private static readonly HashSet<string> IgnoredComponentPropertyNames = new(StringComparer.Ordinal)
        {
            nameof(FlowBloxComponent.Version)
        };

        private static readonly HashSet<string> IgnoredProjectPropertyNames = new(StringComparer.Ordinal)
        {
            nameof(FlowBloxProject.GridSizeX),
            nameof(FlowBloxProject.GridSizeY),
            nameof(FlowBloxProject.AutoIncreaseGridSize)
        };

        public static string CompactTypeName(string typeName)
            => AiAssistantTypeAliasHelper.CompressTypeName(typeName);

        public static bool IsIgnoredProperty(MemberInfo member, JsonProperty property)
        {
            if (member.DeclaringType == typeof(FlowBloxProject))
                return IgnoredProjectPropertyNames.Contains(property.PropertyName);

            // The preview duplicates StringValue and does not apply the password masking rule.
            if (member.DeclaringType == typeof(FieldElement) &&
                property.PropertyName == nameof(FieldElement.ShortStringValue))
                return true;

            return member.DeclaringType == typeof(FlowBloxComponent) &&
                IgnoredComponentPropertyNames.Contains(property.PropertyName);
        }

        public static void WriteEmptyEnumerablesAsKeyword(JsonProperty property)
        {
            if (property.Ignored ||
                property.PropertyType == typeof(string) ||
                !typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                return;
            }

            property.ValueProvider = new EmptyEnumerableKeywordValueProvider(property.ValueProvider);
        }

        public static void WriteEnumerableTypeAsCollectionKeyword(JsonProperty property)
        {
            if (property.Ignored ||
                property.PropertyType == typeof(string) ||
                !typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                return;
            }

            property.Converter = new CollectionKeywordJsonConverter();
        }
    }
}
