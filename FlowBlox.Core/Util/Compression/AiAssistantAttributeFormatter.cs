using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FlowBlox.Core.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Core.Util.Compression
{
    public static class AiAssistantAttributeFormatter
    {
        private static readonly HashSet<Type> ExcludedAttributeTypes =
        [
            typeof(DisplayAttribute),
            typeof(FlowBloxUIAttribute),
            typeof(FlowBloxAssociationAttribute),
            typeof(FlowBloxItemsSourceAttribute),
            typeof(FlowBloxListViewAttribute),
            typeof(FlowBloxDataGridAttribute),
            typeof(FlowBloxTextBoxAttribute)
        ];

        public static string Format(IEnumerable<CustomAttributeData>? attributes)
        {
            return string.Join(",", (attributes ?? Enumerable.Empty<CustomAttributeData>())
                .Where(ShouldInclude)
                .Select(Format));
        }

        internal static string Format(CustomAttributeData attribute)
        {
            ArgumentNullException.ThrowIfNull(attribute);

            var attributeName = attribute.AttributeType.Name;
            const string attributeSuffix = "Attribute";
            if (attributeName.EndsWith(attributeSuffix, StringComparison.Ordinal))
                attributeName = attributeName[..^attributeSuffix.Length];

            var arguments = attribute.ConstructorArguments
                .Select(FormatArgument)
                .Concat(attribute.NamedArguments.Select(
                    argument => $"{argument.MemberName}={FormatArgument(argument.TypedValue)}"))
                .ToList();

            return arguments.Count == 0
                ? attributeName
                : $"{attributeName}({string.Join(";", arguments)})";
        }

        private static bool ShouldInclude(CustomAttributeData attribute)
        {
            return !ExcludedAttributeTypes.Contains(attribute.AttributeType)
                   && !string.Equals(attribute.AttributeType.Name, "JsonIgnoreAttribute", StringComparison.Ordinal);
        }

        private static string FormatArgument(CustomAttributeTypedArgument argument)
        {
            var value = argument.Value;
            if (value == null)
                return "null";

            if (value is Type type)
                return $"typeof({AiAssistantGenericTypeNameHelper.GetCompactTypeName(type)})";

            if (value is IReadOnlyCollection<CustomAttributeTypedArgument> typedArguments)
                return $"[{string.Join(";", typedArguments.Select(FormatArgument))}]";

            if (value is CustomAttributeTypedArgument typedArgument)
                return FormatArgument(typedArgument);

            if (argument.ArgumentType.IsEnum)
            {
                var enumValue = Enum.ToObject(argument.ArgumentType, value);
                var enumMembers = Enum.Format(argument.ArgumentType, enumValue, "F")
                    .Replace(", ", "|", StringComparison.Ordinal);
                return $"{AiAssistantGenericTypeNameHelper.GetCompactTypeName(argument.ArgumentType)}.{enumMembers}";
            }

            if (value is string or char)
                return JsonConvert.SerializeObject(value);

            return JToken.FromObject(value).ToString(Formatting.None);
        }
    }
}
