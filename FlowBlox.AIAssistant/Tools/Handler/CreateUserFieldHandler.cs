using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class CreateUserFieldHandler : ToolHandlerBase
    {
        public override string Name => "CreateUserField";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Creates an Input or Memory field with startup requirements and value-list semantics, ensures Input_/Memory_ prefix naming, and returns resolver metadata. " +
            "Suggestions are examples; PossibleValues restricts non-empty startup values. Required memory must not be cleared at startup. " +
            "Use isPassword for protected Input fields and ask the user to enter the secret locally; never pass secret values to this tool.",
            new JObject
            {
                ["userFieldType"] = "UserFieldTypes (Input|Memory)",
                ["name"] = "string? (optional custom suffix/name; prefix is auto-applied if missing)",
                ["fieldType"] = "FieldTypes? (default Text)",
                ["storeValueLocally"] = "bool? (optional; default true for input fields, false for memory fields)",
                ["isRequiredAtRuntimeStartup"] = "bool? (default false; cannot be true together with clearOnRuntimeStart)",
                ["clearOnRuntimeStart"] = "bool? (optional Memory fields only; default false; cannot be true together with isRequiredAtRuntimeStartup)",
                ["isPassword"] = "bool? (Input only; forces protected local storage; default false)",
                ["syntaxHighlighting"] = "string? (editor language; default Text; e.g. JQL, SQL, JSON, XML)",
                ["loVType"] = "LoVType? (Suggestion|PossibleValues; default Suggestion)",
                ["listOfValues"] = "array? [{displayName:string?,value:string}] (examples or allowed values according to loVType)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var userFieldTypeValue = args.Value<string>("userFieldType");
            if (!Enum.TryParse<UserFieldTypes>(userFieldTypeValue, true, out var userFieldType)
                || userFieldType == UserFieldTypes.None)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    "userFieldType is required and must be one of: Input, Memory."));
            }

            var fieldTypeValue = args.Value<string>("fieldType");
            var fieldType = ParseEnum(fieldTypeValue, FieldTypes.Text);

            var required = args.Value<bool?>("isRequiredAtRuntimeStartup") ?? false;
            var clear = args.Value<bool?>("clearOnRuntimeStart") ?? false;
            var password = args.Value<bool?>("isPassword") ?? false;
            if (clear && (userFieldType != UserFieldTypes.Memory || required))
                return Task.FromResult(ToolHandlerUtilities.Fail("clearOnRuntimeStart is only valid for optional Memory fields."));
            if (password && userFieldType != UserFieldTypes.Input)
                return Task.FromResult(ToolHandlerUtilities.Fail("isPassword is only valid for Input fields."));
            var listType = args.Value<string>("loVType") ?? nameof(LoVType.Suggestion);
            if (!Enum.TryParse<LoVType>(listType, true, out var loVType) || !Enum.IsDefined(loVType))
                return Task.FromResult(ToolHandlerUtilities.Fail("loVType must be Suggestion or PossibleValues."));
            var values = new List<ValueItem>();
            if (args["listOfValues"] is JToken listToken && listToken.Type != JTokenType.Null)
            {
                if (listToken is not JArray list)
                    return Task.FromResult(ToolHandlerUtilities.Fail("listOfValues must be an array."));
                foreach (var item in list)
                {
                    if (item is not JObject entry || entry["value"]?.Type != JTokenType.String)
                        return Task.FromResult(ToolHandlerUtilities.Fail("Each listOfValues entry needs a string value."));
                    values.Add(new ValueItem { DisplayName = entry.Value<string>("displayName"), Value = entry.Value<string>("value") });
                }
            }

            var registry = ToolHandlerUtilities.GetRegistry();
            var requestedName = args.Value<string>("name");
            var fieldPrefix = userFieldType == UserFieldTypes.Input ? "Input_" : "Memory_";
            var resolvedName = ResolveUserFieldName(
                registry,
                fieldPrefix,
                requestedName);

            var field = registry.CreateUserField(userFieldType, fieldType, resolvedName);
            field.IsRequiredAtRuntimeStartup = required;
            field.ClearOnRuntimeStart = clear;
            field.IsPassword = password;
            if (args.TryGetValue("syntaxHighlighting", out var syntaxHighlighting))
                field.SyntaxHighlighting = syntaxHighlighting.Value<string>();
            field.LoVType = loVType;
            foreach (var value in values)
                field.ListOfValues.Add(value);

            var storeValueLocally = args.Value<bool?>("storeValueLocally");
            if (storeValueLocally.HasValue)
                field.StoreValueLocally = storeValueLocally.Value;

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["created"] = true,
                ["name"] = field.Name,
                ["fullyQualifiedFieldName"] = field.FullyQualifiedName,
                ["userFieldType"] = field.UserFieldType.ToString(),
                ["fieldType"] = field.FieldType?.FieldType.ToString() ?? string.Empty,
                ["isRequiredAtRuntimeStartup"] = field.IsRequiredAtRuntimeStartup,
                ["clearOnRuntimeStart"] = field.ClearOnRuntimeStart,
                ["loVType"] = field.LoVType.ToString(),
                ["isPassword"] = field.IsPassword,
                ["syntaxHighlighting"] = field.SyntaxHighlighting,
                ["typeFullName"] = typeof(FieldElement).FullName ?? string.Empty,
                ["resolver"] = new JObject
                {
                    ["resolveFieldElementByFQName"] = field.FullyQualifiedName
                }
            }));
        }

        private static string ResolveUserFieldName(FlowBlox.Core.Provider.Registry.FlowBloxRegistry registry, string requiredPrefix, string? requestedName)
        {
            var normalizedRequested = (requestedName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedRequested))
            {
                var prefixed = normalizedRequested.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase)
                    ? normalizedRequested
                    : requiredPrefix + normalizedRequested;

                return EnsureUniqueUserFieldName(registry, prefixed, "_");
            }

            return GetNextNumberedUserFieldName(registry, requiredPrefix);
        }

        private static string EnsureUniqueUserFieldName(FlowBlox.Core.Provider.Registry.FlowBloxRegistry registry, string candidate, string separator)
        {
            if (!registry.HasUserField(candidate))
                return candidate;

            var suffix = 1;
            while (registry.HasUserField(candidate + separator + suffix))
                suffix++;

            return candidate + separator + suffix;
        }

        private static string GetNextNumberedUserFieldName(FlowBlox.Core.Provider.Registry.FlowBloxRegistry registry, string prefix)
        {
            var index = 0;
            while (registry.HasUserField(prefix + index))
                index++;

            return prefix + index;
        }

        private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<TEnum>(value, true, out var parsed))
                return parsed;

            return fallback;
        }
    }
}
