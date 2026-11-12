using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Json.ValueProvider;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace FlowBlox.Core.Util.Json.PropertyCreator
{
    /// <summary>
    /// Adds import-only aliases for legacy scalar companion field properties.
    /// </summary>
    internal static class LegacySelectedFieldPropertyCreator
    {
        public static void AddProperties(Type type, IList<JsonProperty> properties)
        {
            if (!typeof(FlowBloxReactiveObject).IsAssignableFrom(type))
                return;

            foreach (var member in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!FlowBloxFieldSelection.SupportsScalar(member.PropertyType) || !FlowBloxFieldSelection.Supports(member))
                    continue;

                var legacyName = member.Name + "_SelectedField";
                if (properties.Any(property => property.PropertyName == legacyName))
                    continue;

                properties.Add(new JsonProperty
                {
                    PropertyName = legacyName,
                    PropertyType = typeof(FieldElement),
                    DeclaringType = type,
                    Writable = true,
                    Readable = false,
                    ValueProvider = new LegacySelectedFieldValueProvider(member.Name)
                });
            }
        }
    }
}
