using FlowBlox.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.Compression;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static partial class ToolHandlerUtilities
    {
        internal const string TypeInfoCacheDescription =
            "Includes base members and nested ReactiveObjects. Repeated retrieval promotes complete declaring types to runtime system messages. " +
            "omitted=[{type,properties}] means promoted; type identifies the defining declaration. A repeated requested type returns only a promotion notice. " +
            "Conflicting inherited/overridden property names include declaringType. Promotions reset after application restart.";

        public static ToolResponse CreateUnifiedTypeInfoResponse(string? fullName)
        {
            var type = ResolveType(fullName);
            if (type == null)
                return Fail($"Type '{fullName}' could not be resolved.");

            if (!type.IsEnum && !typeof(FlowBloxReactiveObject).IsAssignableFrom(type))
                return Fail($"Type '{fullName}' is not supported for kind info. Only FlowBloxReactiveObject types and Enums are supported.");

            var cache = CurrentContentCache;
            if (cache.IsTypePromoted(type.FullName!))
                return Ok(CompressTypeAliases(AssistantSessionContentCache.PromotionNotice(type.FullName!).Result));

            var descriptions = new Dictionary<string, JObject>(StringComparer.Ordinal);
            if (type.IsEnum)
                descriptions[type.FullName!] = BuildEnumKindInfo(type);
            else
                BuildTypeKindInfo(
                    type,
                    true,
                    descriptions);

            var declaringTypes = descriptions.Keys.ToArray();
            cache.RegisterDeclaringTypes(
                declaringTypes,
                declaringType => new AssistantSystemMessage(
                    "type:" + declaringType,
                    declaringType,
                    AssistantSystemMessageKind.TypeInfo,
                    CompressTypeAliases(RenderTypePayload(
                        declaringType,
                        descriptions,
                        cache,
                        true)).ToString(Newtonsoft.Json.Formatting.None),
                    true));
            var response = Ok(new JObject());
            response.RefreshBeforeSerialization = () =>
                response.Result = CompressTypeAliases(RenderTypePayload(
                    type.FullName!,
                    descriptions,
                    cache,
                    false));
            response.RefreshForSerialization();
            return response;
        }

        private static JObject RenderTypePayload(
            string type,
            Dictionary<string, JObject> descriptions,
            AssistantSessionContentCache cache,
            bool systemDeclaration)
        {
            if (!systemDeclaration && cache.IsTypePromoted(type))
                return AssistantSessionContentCache.PromotionNotice(type).Result;

            var kind = RenderTypeKind(
                type,
                descriptions,
                cache,
                systemDeclaration);
            var payload = new JObject
            {
                ["kind"] = kind
            };
            if (systemDeclaration)
                return payload;

            var hierarchy = descriptions[type]["inheritanceHierarchy"]?.Values<string>()
                .ToHashSet(StringComparer.Ordinal) ?? [];
            hierarchy.Add(type);
            var referenced = descriptions.Values.SelectMany(x => x["usedTypes"]?.Children<JObject>() ?? [])
                .Select(x => x.Value<string>("fullName"))
                .ToHashSet(StringComparer.Ordinal);
            var additional = new JArray(descriptions.Keys.Where(x => !hierarchy.Contains(x) && referenced.Contains(x))
                .Select(x => cache.IsTypePromoted(x)
                ? new JObject
                {
                    ["fullName"] = x,
                    ["message"] = "Provided as a runtime session system message."
                }
                : RenderTypeKind(
                    x,
                    descriptions,
                    cache,
                    false)));
            if (additional.Count > 0)
                payload["additionalTypeKindsInfo"] = additional;

            return payload;
        }

        private static JObject RenderTypeKind(
            string type,
            Dictionary<string, JObject> descriptions,
            AssistantSessionContentCache cache,
            bool systemDeclaration)
        {
            var result = (JObject)descriptions[type].DeepClone();
            if (result["properties"] == null)
                return result; // Enum metadata.

            var hierarchy = result["inheritanceHierarchy"]?.Values<string>() ?? [];
            var declarations = hierarchy.Append(type)
                .Distinct(StringComparer.Ordinal)
                .Where(descriptions.ContainsKey)
                .ToArray();
            var conflicts = declarations.SelectMany(x => descriptions[x]["properties"]?.Children<JObject>() ?? [])
                .GroupBy(x => x.Value<string>("name"))
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToHashSet(StringComparer.Ordinal);
            var properties = new List<JObject>();
            var omitted = new JArray();
            var usedTypes = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var declaringType in declarations)
            {
                var declaration = descriptions[declaringType];
                var declaredProperties = declaration["properties"]?.Children<JObject>()
                    .ToArray() ?? [];
                if (cache.IsTypePromoted(declaringType) && !(systemDeclaration && declaringType == type))
                {
                    if (declaredProperties.Length > 0)
                        omitted.Add(new JObject
                        {
                            ["type"] = declaringType,
                            ["properties"] = new JArray(declaredProperties.Select(x => x.Value<string>("name")))
                        });

                    continue;
                }

                foreach (var property in declaredProperties)
                {
                    var copy = (JObject)property.DeepClone();
                    if (conflicts.Contains(copy.Value<string>("name")))
                        copy["declaringType"] = declaringType;

                    properties.Add(copy);
                }

                foreach (var used in declaration["usedTypes"]?.Children<JObject>() ?? [])
                    usedTypes.TryAdd(used.Value<string>("fullName")!, used.DeepClone());
            }

            result["properties"] = new JArray(properties.OrderBy(x => x.Value<string>("name")));
            result.Remove("usedTypes");
            if (usedTypes.Count > 0)
                result["usedTypes"] = new JArray(usedTypes.Values);

            if (omitted.Count > 0)
                result["omitted"] = omitted;

            return result;
        }

        private static JObject CompressTypeAliases(JObject payload)
        {
            foreach (var value in payload.DescendantsAndSelf()
                .OfType<JValue>())
                if (value.Type == JTokenType.String && value.Value is string text)
                    value.Value = AiAssistantTypeAliasHelper.CompressTypeName(text);

            return payload;
        }

        public static IEnumerable<JObject> GetSupportedTypeInfos(Type baseType)
        {
            var supportedTypes = new Dictionary<string, JObject>(StringComparer.Ordinal);

            if (!baseType.IsAbstract && !baseType.IsInterface)
            {
                AddSupportedTypeInfo(supportedTypes, baseType);
            }

            foreach (var flowBlock in GetProject().CreateInstances<BaseFlowBlock>())
            {
                var type = flowBlock.GetType();
                if (!baseType.IsAssignableFrom(type) || type.IsAbstract)
                {
                    continue;
                }

                AddSupportedTypeInfo(supportedTypes, type);
            }

            foreach (var managedObject in GetProject().CreateInstances<IManagedObject>())
            {
                var type = managedObject.GetType();
                if (!baseType.IsAssignableFrom(type) || type.IsAbstract)
                {
                    continue;
                }

                AddSupportedTypeInfo(supportedTypes, type);
            }

            foreach (var type in EnumerateLoadableTypes())
            {
                if (!baseType.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                if (FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(baseType) &&
                    !FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(type))
                {
                    continue;
                }

                AddSupportedTypeInfo(supportedTypes, type);
            }

            return supportedTypes.Values
                .OrderBy(x => x.Value<string>("displayName"))
                .ThenBy(x => x.Value<string>("fullName"));
        }

        private static JObject BuildTypeKindInfo(
            Type type,
            bool includeChildren,
            Dictionary<string, JObject> descriptions)
        {
            var key = type.FullName ?? type.Name;
            if (descriptions.TryGetValue(key, out var known))
                return known;

            var metadata = GetTypeDisplayMetadata(type);
            var result = new JObject
            {
                ["fullName"] = key,
                ["displayName"] = metadata.DisplayName,
                ["description"] = metadata.Description,
                ["inheritanceHierarchy"] = new JArray(GetTypeHierarchy(type))
            };

            descriptions.Add(key, result); // Guard recursive ReactiveObject graphs.
            if (type.BaseType != null && typeof(FlowBloxReactiveObject).IsAssignableFrom(type.BaseType))
                BuildTypeKindInfo(
                    type.BaseType,
                    includeChildren,
                    descriptions);

            var usedTypes = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var properties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p => ShouldIncludeProperty(type, p))
                .OrderBy(p => p.Name)
                .ToArray();

            result["properties"] = new JArray(properties.Select(p => ToPropertyInfo(
                p,
                includeChildren,
                descriptions,
                usedTypes)));

            if (usedTypes.Count > 0)
                result["usedTypes"] = new JArray(usedTypes.Values.OrderBy(x => x.Value<string>("fullName")));

            var explanations = BuildSpecialExplanations(type);
            if (explanations.Count > 0)
                result["specialExplanations"] = explanations;

            return result;
        }

        private static JObject ToPropertyInfo(
            PropertyInfo property,
            bool includeChildren,
            Dictionary<string, JObject> additionalKinds,
            Dictionary<string, JObject> usedTypes)
        {
            var displayAttribute = property.GetCustomAttribute<DisplayAttribute>();
            var displayName = displayAttribute != null
                ? FlowBloxResourceUtil.GetDisplayName(displayAttribute, false)
                : property.Name;
            var description = displayAttribute != null
                ? FlowBloxResourceUtil.GetDescription(displayAttribute)
                : string.Empty;

            var propertyType = property.PropertyType;
            var elementType = GetElementType(propertyType);
            var actualType = elementType ?? propertyType;
            var nonNullableType = Nullable.GetUnderlyingType(actualType) ?? actualType;

            var isCollection = elementType != null;
            var isManagedObject = typeof(IManagedObject).IsAssignableFrom(nonNullableType);
            var isFieldElement = typeof(FieldElement).IsAssignableFrom(nonNullableType);
            var isReactiveObject = typeof(FlowBloxReactiveObject).IsAssignableFrom(nonNullableType);
            var isFlowBlockReference = typeof(BaseFlowBlock).IsAssignableFrom(nonNullableType);
            var isEnum = nonNullableType.IsEnum;
            var isSimple = IsSimpleType(nonNullableType);
            var isImmutable = IsImmutableProperty(property);
            var nullable = IsNullable(property);
            var majorTypeDescriptor = GetMajorTypeDescriptor(nonNullableType, "SimpleProperty");

            var flowBlockUi = property.GetCustomAttribute<FlowBloxUIAttribute>();

            var propertyInfo = new JObject
            {
                ["name"] = property.Name,
                ["displayName"] = string.IsNullOrWhiteSpace(displayName) ? property.Name : displayName,
                ["type"] = AiAssistantGenericTypeNameHelper.GetCompactTypeName(propertyType),
                ["flags"] = BuildPropertyFlags(nullable, property.CanWrite && !isImmutable, isSimple, isEnum, isCollection),
                ["majorTypeDescriptor"] = majorTypeDescriptor,
                ["canUpdateViaParentPath"] = !isManagedObject && !isImmutable
            };

            if (isImmutable)
                propertyInfo["readOnly"] = true;

            if (!string.IsNullOrWhiteSpace(description))
                propertyInfo["description"] = description;

            if (flowBlockUi != null)
            {
                var uiFlags = BuildUiFlags(flowBlockUi.Operations, flowBlockUi.UiOptions, FlowBloxFieldSelection.Supports(property));
                if (!string.IsNullOrEmpty(uiFlags))
                    propertyInfo["uiFlags"] = uiFlags;

                var uiInfo = new JObject();

                var creatableTypes = new JArray((flowBlockUi.CreatableTypes ?? Array.Empty<Type>()).Select(x => x.FullName ?? x.Name));
                if (creatableTypes.Count > 0)
                    uiInfo["creatableTypes"] = creatableTypes;

                if (flowBlockUi.ReadOnly)
                    uiInfo["readOnly"] = true;

                if (!string.IsNullOrWhiteSpace(flowBlockUi.ToolboxCategory))
                    uiInfo["toolboxCategory"] = flowBlockUi.ToolboxCategory;

                if (uiInfo.HasValues)
                    propertyInfo["ui"] = uiInfo;
            }

            if (isFlowBlockReference)
                propertyInfo["isFlowBlockReference"] = true;

            var attributes = AiAssistantAttributeFormatter.Format(property.GetCustomAttributesData());
            if (!string.IsNullOrEmpty(attributes))
                propertyInfo["attributes"] = attributes;

            if (isEnum)
            {
                propertyInfo["enumMembers"] = new JArray(Enum.GetNames(nonNullableType));
                AddUsedEnumType(usedTypes, nonNullableType);
            }

            if ((propertyType.IsInterface || propertyType.IsAbstract || isManagedObject || isFlowBlockReference)
                && !isFlowBlockReference
                && !isSimple)
            {
                var supportedTypes = GetSupportedTypeInfos(nonNullableType);
                propertyInfo["supportedTypes"] = new JArray(supportedTypes);
            }

            if (includeChildren && FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(nonNullableType) && !isManagedObject && !isSimple)
            {
                var key = nonNullableType.FullName ?? nonNullableType.Name;
                if (!additionalKinds.ContainsKey(key))
                {
                    additionalKinds[key] = BuildTypeKindInfo(
                        nonNullableType,
                        includeChildren: false,
                        additionalKinds);
                }
            }

            if (FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(nonNullableType))
            {
                AddUsedReactiveObjectType(usedTypes, nonNullableType);
            }


            return propertyInfo;
        }

        private static string BuildPropertyFlags(
            bool nullable,
            bool canWrite,
            bool isSimple,
            bool isEnum,
            bool isCollection)
        {
            var flags = string.Empty;

            if (nullable)
                flags += "N";
            if (canWrite)
                flags += "W";
            if (isSimple)
                flags += "S";
            if (isEnum)
                flags += "E";
            if (isCollection)
                flags += "C";

            return flags;
        }

        internal static string BuildUiFlags(UIOperations operations, UIOptions options, bool supportsFields = false)
        {
            var flags = string.Empty;

            if (operations.HasFlag(UIOperations.Create))
                flags += "C";
            if (operations.HasFlag(UIOperations.Link))
                flags += "L";
            if (operations.HasFlag(UIOperations.Unlink))
                flags += "U";
            if (operations.HasFlag(UIOperations.Edit))
                flags += "E";
            if (operations.HasFlag(UIOperations.Delete))
                flags += "D";

            if (supportsFields)
                flags += "[FLD]";
            if (options.HasFlag(UIOptions.EnableFileSelection))
                flags += "[FL]";
            if (options.HasFlag(UIOptions.EnableFolderSelection))
                flags += "[DI]";

            return flags;
        }

        private static void AddSupportedTypeInfo(IDictionary<string, JObject> target, Type type)
        {
            var key = type.FullName ?? type.Name;
            if (target.ContainsKey(key))
            {
                return;
            }

            var typeDisplayMetadata = GetTypeDisplayMetadata(type);

            target[key] = new JObject
            {
                ["fullName"] = key,
                ["displayName"] = typeDisplayMetadata.DisplayName,
                ["description"] = typeDisplayMetadata.Description,
                ["majorTypeDescriptor"] = GetMajorTypeDescriptor(type)
            };
        }

        private static (string DisplayName, string Description) GetTypeDisplayMetadata(Type type)
        {
            var displayAttribute = type.GetCustomAttribute<DisplayAttribute>();
            if (displayAttribute == null)
            {
                return (type.Name, string.Empty);
            }

            var displayName = type.Name;
            var description = string.Empty;

            try
            {
                var localizedDisplayName = FlowBloxResourceUtil.GetDisplayName(displayAttribute, false);
                if (!string.IsNullOrWhiteSpace(localizedDisplayName))
                {
                    displayName = localizedDisplayName;
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to resolve a localized display name for AI Assistant type metadata.",
                    ex);
            }

            try
            {
                description = FlowBloxResourceUtil.GetDescription(displayAttribute) ?? string.Empty;
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to resolve a localized description for AI Assistant type metadata.",
                    ex);
            }

            return (displayName, description);
        }

        private static void AddUsedEnumType(IDictionary<string, JObject> usedTypes, Type enumType)
        {
            var key = enumType.FullName ?? enumType.Name;
            if (usedTypes.ContainsKey(key))
            {
                return;
            }

            usedTypes[key] = new JObject
            {
                ["fullName"] = key,
                ["kind"] = "enum",
                ["enumMembers"] = new JArray(Enum.GetNames(enumType))
            };
        }

        private static void AddUsedReactiveObjectType(IDictionary<string, JObject> usedTypes, Type reactiveType)
        {
            var key = reactiveType.FullName ?? reactiveType.Name;
            if (usedTypes.ContainsKey(key))
            {
                return;
            }

            var supported = GetSupportedTypeInfos(reactiveType);
            usedTypes[key] = new JObject
            {
                ["fullName"] = key,
                ["kind"] = "reactiveObject",
                ["inheritanceHierarchy"] = new JArray(GetTypeHierarchy(reactiveType)),
                ["supportedTypes"] = new JArray(supported)
            };
        }

        private static JObject BuildEnumKindInfo(Type enumType)
        {
            return new JObject
            {
                ["fullName"] = enumType.FullName ?? enumType.Name,
                ["displayName"] = enumType.Name,
                ["description"] = $"Enum type '{enumType.Name}'.",
                ["inheritanceHierarchy"] = new JArray(GetTypeHierarchy(enumType)),
                ["properties"] = new JArray(),
                ["usedTypes"] = new JArray
                {
                    new JObject
                    {
                        ["fullName"] = enumType.FullName ?? enumType.Name,
                        ["kind"] = "enum",
                        ["enumMembers"] = new JArray(Enum.GetNames(enumType))
                    }
                }
            };
        }

        private static JArray BuildSpecialExplanations(Type type)
        {
            var entries = type
                .GetCustomAttributes(typeof(FlowBloxSpecialExplanationAttribute), inherit: true)
                .OfType<FlowBloxSpecialExplanationAttribute>()
                .Select(x => new
                {
                    SpecialExplanation = x.GetResolvedSpecialExplanation(),
                    x.Icon
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.SpecialExplanation))
                .ToList();

            if (!entries.Any())
            {
                return new JArray();
            }

            var flatEntries = entries
                .Select(entry => new JObject
                {
                    ["icon"] = entry.Icon.ToString(),
                    ["explanation"] = entry.SpecialExplanation
                });

            return new JArray(flatEntries);
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive
                   || type.IsEnum
                   || type == typeof(string)
                   || type == typeof(decimal)
                   || type == typeof(DateTime)
                   || type == typeof(Guid)
                   || type == typeof(DateTimeOffset)
                   || type == typeof(TimeSpan);
        }

        private static bool IsNullable(PropertyInfo property)
        {
            var type = property.PropertyType;
            if (Nullable.GetUnderlyingType(type) != null)
            {
                return true;
            }

            if (!type.IsValueType)
            {
                try
                {
                    var info = _nullabilityInfoContext.Create(property);
                    return info.ReadState == NullabilityState.Nullable || info.WriteState == NullabilityState.Nullable;
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Failed to resolve nullability metadata for property '{property.Name}'.",
                        ex);
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetTypeHierarchy(Type type)
        {
            var chain = new List<string>();
            var current = type;
            while (current != null)
            {
                if (current == typeof(object))
                {
                    break;
                }

                chain.Add(current.FullName ?? current.Name);
                current = current.BaseType;
            }

            return chain;
        }

        private static string? GetMajorTypeDescriptor(Type type, string? defaultMajorTypeDescriptor = null)
        {
            if (typeof(FieldElement).IsAssignableFrom(type))
                return "FieldElement";

            if (typeof(IManagedObject).IsAssignableFrom(type))
                return "ManagedObject";

            if (typeof(BaseFlowBlock).IsAssignableFrom(type))
                return "FlowBlock";

            if (typeof(FlowBloxReactiveObject).IsAssignableFrom(type))
                return "ReactiveObject";

            return defaultMajorTypeDescriptor;
        }

        public static string GetMajorTypeDescriptorForType(Type type, string defaultMajorTypeDescriptor = "ReactiveObject")
        {
            return GetMajorTypeDescriptor(type, defaultMajorTypeDescriptor) ?? defaultMajorTypeDescriptor;
        }

        private static bool ShouldIncludeProperty(
            Type ownerType,
            PropertyInfo property)
        {
            if (HasJsonIgnore(property))
            {
                return false;
            }

            if ((typeof(FlowBloxComponent).IsAssignableFrom(ownerType) || ownerType == typeof(FlowBloxReactiveObject))
                && ExcludedFlowBloxComponentProperties.Contains(property.Name))
            {
                return false;
            }

            if (typeof(BaseFlowBlock).IsAssignableFrom(ownerType)
                && ExcludedBaseFlowBlockProperties.Contains(property.Name))
            {
                return false;
            }

            return true;
        }

        private static bool HasJsonIgnore(PropertyInfo property)
        {
            return property.GetCustomAttributesData()
                .Any(x => string.Equals(x.AttributeType.Name, "JsonIgnoreAttribute", StringComparison.Ordinal));
        }

        private static IEnumerable<Type> EnumerateLoadableTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Some types could not be loaded from assembly '{assembly.FullName}' while resolving AI Assistant type metadata.",
                        ex);
                    types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Failed to enumerate types from assembly '{assembly.FullName}' while resolving AI Assistant type metadata.",
                        ex);
                    continue;
                }

                foreach (var type in types)
                {
                    yield return type;
                }
            }
        }
    }
}
