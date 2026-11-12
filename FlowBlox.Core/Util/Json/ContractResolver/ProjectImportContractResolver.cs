using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.Json.PropertyCreator;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace FlowBlox.Core.Util.Json.ContractResolver
{
    internal sealed class ProjectImportContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);
            LegacySelectedFieldPropertyCreator.AddProperties(type, properties);
            return properties;
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (member.DeclaringType == typeof(BaseFlowBlock) && 
                string.Equals(property.PropertyName, nameof(BaseFlowBlock.Size), StringComparison.Ordinal))
            {
                property.Ignored = true;
            }

            if (member.DeclaringType == typeof(BaseFlowBlock) &&
                string.Equals(property.PropertyName, nameof(BaseFlowBlock.NextFlowBlocks), StringComparison.Ordinal))
            {
                property.Ignored = true;
            }

            return property;
        }
    }
}
