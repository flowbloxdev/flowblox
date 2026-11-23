using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Drawing;
using System.Collections;
using System.Reflection;

namespace FlowBlox.Core.Util.Json.ContractResolver
{
    internal sealed class ComponentSnapshotContractResolver : DefaultContractResolver
    {
        private readonly bool _compact;

        public ComponentSnapshotContractResolver(bool compact = true)
        {
            _compact = compact;
        }

        private static readonly HashSet<string> IgnoredPropertyNames = new(StringComparer.Ordinal)
        {
            nameof(FlowBloxReactiveObject.Icon16),
            nameof(FlowBloxReactiveObject.Icon32),
            nameof(FlowBloxReactiveObject.HasErrors)
        };

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (IgnoredPropertyNames.Contains(property.PropertyName))
            {
                property.Ignored = true;
                return property;
            }

            if (AiAssistantJsonPropertySerializationRules.IsIgnoredProperty(member, property))
            {
                property.Ignored = true;
                return property;
            }

            var propertyType = property.PropertyType;
            if (!IsSerializableComponentProperty(propertyType))
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

            if (_compact)
            {
                AiAssistantJsonPropertySerializationRules.WriteEmptyEnumerablesAsKeyword(property);
                AiAssistantJsonPropertySerializationRules.WriteEnumerableTypeAsCollectionKeyword(property);
            }

            return property;
        }

        private static bool IsSerializableComponentProperty(Type propertyType)
        {
            if (propertyType == typeof(string))
                return true;

            var nonNullable = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (nonNullable.IsPrimitive || 
                nonNullable.IsEnum || 
                nonNullable == typeof(decimal) || 
                nonNullable == typeof(DateTime) || 
                nonNullable == typeof(DateTimeOffset) || 
                nonNullable == typeof(Guid) || 
                nonNullable == typeof(TimeSpan))
            {
                return true;
            }
            
            if (nonNullable == typeof(Point) || nonNullable == typeof(Size))
                return true;

            if (typeof(FieldElement).IsAssignableFrom(nonNullable) ||
                typeof(BaseFlowBlock).IsAssignableFrom(nonNullable) ||
                typeof(IManagedObject).IsAssignableFrom(nonNullable))
            {
                return true;
            }

            if (typeof(FlowBloxReactiveObject).IsAssignableFrom(nonNullable))
                return true;

            if (typeof(IEnumerable).IsAssignableFrom(nonNullable))
                return true;

            return false;
        }
    }
}
