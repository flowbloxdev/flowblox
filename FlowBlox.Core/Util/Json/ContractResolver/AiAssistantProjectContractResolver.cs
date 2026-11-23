using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace FlowBlox.Core.Util.Json.ContractResolver
{
    internal sealed class AiAssistantProjectContractResolver : ProjectContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (member.DeclaringType == typeof(FlowBloxProject) &&
                string.Equals(property.PropertyName, "InputFiles", StringComparison.Ordinal))
            {
                property.Ignored = true;
            }

            if (AiAssistantJsonPropertySerializationRules.IsIgnoredProperty(member, property))
            {
                property.Ignored = true;
                return property;
            }

            if (member.DeclaringType == typeof(FieldElement) &&
                string.Equals(property.PropertyName, nameof(FieldElement.StringValue), StringComparison.Ordinal))
            {
                JsonPropertyShouldSerializeHelper.ChainShouldSerialize(
                    property,
                    instance => instance is not FieldElement field || !field.IsPassword);
            }

            AiAssistantJsonPropertySerializationRules.WriteEmptyEnumerablesAsKeyword(property);
            AiAssistantJsonPropertySerializationRules.WriteEnumerableTypeAsCollectionKeyword(property);

            return property;
        }
    }
}
