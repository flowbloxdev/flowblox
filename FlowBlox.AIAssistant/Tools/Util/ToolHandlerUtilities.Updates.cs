using FlowBlox.Core.Extensions;
using System.Collections;
using System.Reflection;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Deletion;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.FlowBlocks;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static partial class ToolHandlerUtilities
    {
        private static readonly AsyncLocal<List<DeletionReport>?> CurrentDeletionReports = new();

        public static ToolResponse ApplyUpdates(JObject args, object target, FlowBloxRegistry registry, string kind, string name)
        {
            var updates = CollectUpdates(args);
            if (updates.Count == 0)
            {
                return Fail("No update operations provided. Use path/value or updates[].");
            }

            var applied = new JArray();
            CurrentDeletionReports.Value = new List<DeletionReport>();

            foreach (var update in updates)
            {
                try
                {
                    SetByJsonPath(target, update.Path, update.Value, registry);
                    applied.Add(new JObject
                    {
                        ["path"] = update.Path,
                        ["ok"] = true
                    });
                }
                catch (Exception ex)
                {
                    applied.Add(new JObject
                    {
                        ["path"] = update.Path,
                        ["ok"] = false,
                        ["error"] = ex.Message
                    });

                    var payload = new JObject
                    {
                        ["updated"] = false,
                        ["kind"] = kind,
                        ["name"] = name,
                        ["applied"] = applied
                    };
                    AddCombinedDeletionReportIfAny(payload, CurrentDeletionReports.Value);

                    CurrentDeletionReports.Value = null;
                    return Fail($"Update failed at path '{update.Path}': {ex.Message}", payload);
                }
            }

            if (target is IFlowBloxComponent component)
            {
                component.OnAfterSave();
            }

            var result = new JObject
            {
                ["updated"] = true,
                ["kind"] = kind,
                ["name"] = name,
                ["applied"] = applied,
                ["count"] = applied.Count
            };
            AddCombinedDeletionReportIfAny(result, CurrentDeletionReports.Value);
            CurrentDeletionReports.Value = null;
            return Ok(result);
        }

        private static List<UpdateOperation> CollectUpdates(JObject args)
        {
            var updates = new List<UpdateOperation>();

            var singlePath = args.Value<string>("path");
            if (!string.IsNullOrWhiteSpace(singlePath))
            {
                var singleValue = args["value"];
                if (singleValue != null || IsUnaryOperationPath(singlePath))
                {
                    updates.Add(new UpdateOperation
                    {
                        Path = singlePath,
                        Value = singleValue ?? JValue.CreateNull()
                    });
                }
            }

            if (args["updates"] is JArray updatesArray)
            {
                foreach (var updateItem in updatesArray.OfType<JObject>())
                {
                    var updatePath = updateItem.Value<string>("path");
                    if (string.IsNullOrWhiteSpace(updatePath))
                    {
                        continue;
                    }

                    var updateValue = updateItem["value"];
                    if (updateValue == null && !IsUnaryOperationPath(updatePath))
                    {
                        continue;
                    }

                    updates.Add(new UpdateOperation
                    {
                        Path = updatePath,
                        Value = updateValue ?? JValue.CreateNull()
                    });
                }
            }

            return updates;
        }

        private static bool IsUnaryOperationPath(string path)
        {
            var segments = ParsePath(path);
            if (segments.Count == 0)
                return false;

            var operation = ResolveTerminalOperation(segments);
            return operation == TerminalOperation.Delete || operation == TerminalOperation.Unlink;
        }

        private static void SetByJsonPath(object root, string path, JToken value, FlowBloxRegistry registry)
        {
            var pathSegments = ParsePath(path);
            if (pathSegments.Count == 0)
            {
                throw new InvalidOperationException("Path is empty.");
            }

            var operation = ResolveTerminalOperation(pathSegments);
            if (operation != TerminalOperation.Set)
            {
                pathSegments.RemoveAt(pathSegments.Count - 1);
                if (pathSegments.Count == 0)
                {
                    throw new InvalidOperationException("Path must target a property or collection element before Link/Unlink/Delete.");
                }
            }

            object current = root;
            object? currentCollectionOwner = null;
            for (var i = 0; i < pathSegments.Count; i++)
            {
                var segment = pathSegments[i];
                var isLast = i == pathSegments.Count - 1;

                if (segment.IsProperty)
                {
                    var property = current.GetType().GetProperty(segment.PropertyName!, BindingFlags.Public | BindingFlags.Instance);
                    if (property == null)
                        throw new InvalidOperationException($"Property '{segment.PropertyName}' not found on '{current.GetType().FullName}'.");

                    EnsurePropertyWriteAccess(property);

                    var propertyType = property.PropertyType;
                    var propertyTargetType = GetElementType(propertyType) ?? propertyType;
                    if (!isLast && IsReferenceComponentType(propertyTargetType))
                    {
                        throw new InvalidOperationException(
                            $"Path segment '{segment.PropertyName}' targets a {GetReferenceComponentLabel(propertyTargetType)}. " +
                            "Indirect updates are not allowed. " +
                            $"{GetDirectUpdateToolHint(propertyTargetType)}");
                    }

                    if (isLast)
                    {
                        ApplyPropertyTerminalOperation(current, currentCollectionOwner, property, operation, value, registry, path);
                        return;
                    }

                    var propertyValue = property.GetValue(current);
                    if (propertyValue == null)
                    {
                        propertyValue = CreateInstance(property.PropertyType);
                        property.SetValue(current, propertyValue);
                    }

                    currentCollectionOwner = propertyValue is IList ? current : null;
                    current = propertyValue;
                    continue;
                }

                if (current is not IList list)
                {
                    throw new InvalidOperationException($"Index segment '{segment.Index}' requires list-like target.");
                }

                var elementType = GetElementType(current.GetType())
                                  ?? throw new InvalidOperationException("Could not infer list element type.");
                if (!isLast && IsReferenceComponentType(elementType))
                {
                    throw new InvalidOperationException(
                        $"Path segment '[{segment.Index}]' targets a {GetReferenceComponentLabel(elementType)}. " +
                        "Indirect updates are not allowed. " +
                        $"{GetDirectUpdateToolHint(elementType)}");
                }

                var explicitElementType = ResolveExplicitCollectionElementType(elementType, segment, path);

                if (isLast)
                {
                    ApplyIndexedTerminalOperation(
                        list,
                        elementType,
                        explicitElementType,
                        segment.Index,
                        operation,
                        value,
                        registry,
                        path,
                        currentCollectionOwner);
                    return;
                }

                while (list.Count <= segment.Index)
                {
                    list.Add(CreateCollectionElementInstance(elementType, explicitElementType, currentCollectionOwner));
                }

                var indexedValue = list[segment.Index];
                if (indexedValue == null)
                {
                    indexedValue = CreateCollectionElementInstance(elementType, explicitElementType, currentCollectionOwner);
                    list[segment.Index] = indexedValue;
                }

                if (explicitElementType != null && indexedValue.GetType() != explicitElementType)
                {
                    throw new InvalidOperationException(
                        $"Path segment '[{segment.Index}:{segment.ExplicitTypeFullName}]' resolved existing type '{indexedValue.GetType().FullName}', " +
                        $"expected '{explicitElementType.FullName}'. Delete this element first, then create the desired type.");
                }

                current = indexedValue;
                currentCollectionOwner = null;
            }
        }

        private static List<PathSegment> ParsePath(string rawPath)
        {
            var path = rawPath?.Trim() ?? string.Empty;
            var segments = new List<PathSegment>();

            if (string.IsNullOrWhiteSpace(path))
            {
                return segments;
            }

            if (path.StartsWith('/'))
            {
                foreach (var token in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TryParseSlashIndexToken(token, out var index, out var explicitTypeFullName))
                    {
                        segments.Add(new PathSegment
                        {
                            IsProperty = false,
                            Index = index,
                            ExplicitTypeFullName = explicitTypeFullName
                        });
                    }
                    else
                    {
                        segments.Add(new PathSegment { IsProperty = true, PropertyName = token });
                    }
                }

                return segments;
            }

            foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedPart = part.Trim();
                var bracketStart = trimmedPart.IndexOf('[');

                if (bracketStart < 0)
                {
                    segments.Add(new PathSegment
                    {
                        IsProperty = true,
                        PropertyName = trimmedPart
                    });
                    continue;
                }

                segments.Add(new PathSegment
                {
                    IsProperty = true,
                    PropertyName = trimmedPart.Substring(0, bracketStart)
                });

                var remaining = trimmedPart.Substring(bracketStart);
                while (remaining.StartsWith("[", StringComparison.Ordinal))
                {
                    var bracketEnd = remaining.IndexOf(']');
                    var indexText = remaining.Substring(1, bracketEnd - 1);
                    if (!int.TryParse(indexText, out var index))
                    {
                        throw new InvalidOperationException($"Invalid index '{indexText}'.");
                    }

                    segments.Add(new PathSegment
                    {
                        IsProperty = false,
                        Index = index
                    });

                    remaining = remaining.Substring(bracketEnd + 1);
                }
            }

            return segments;
        }

        private static bool TryParseSlashIndexToken(string token, out int index, out string? explicitTypeFullName)
        {
            index = -1;
            explicitTypeFullName = null;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var colonIndex = token.IndexOf(':');
            var indexToken = colonIndex < 0 ? token : token.Substring(0, colonIndex);
            if (!int.TryParse(indexToken, out index))
                return false;

            if (colonIndex < 0)
                return true;

            explicitTypeFullName = token.Substring(colonIndex + 1).Trim();
            if (string.IsNullOrWhiteSpace(explicitTypeFullName))
            {
                throw new InvalidOperationException(
                    $"Invalid typed index segment '{token}'. Expected format '/<index>:<FullTypeName>'.");
            }

            return true;
        }

        private static Type? ResolveExplicitCollectionElementType(Type elementType, PathSegment segment, string path)
        {
            if (string.IsNullOrWhiteSpace(segment.ExplicitTypeFullName))
                return null;

            var explicitType = ResolveType(segment.ExplicitTypeFullName);
            if (explicitType == null)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' contains unknown type '{segment.ExplicitTypeFullName}' in typed index segment.");
            }

            if (!elementType.IsAssignableFrom(explicitType))
            {
                throw new InvalidOperationException(
                    $"Path '{path}' typed index '{segment.Index}:{segment.ExplicitTypeFullName}' is invalid. " +
                    $"Type '{explicitType.FullName}' is not assignable to '{elementType.FullName}'.");
            }

            if (explicitType.IsAbstract || explicitType.IsInterface)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' typed index uses abstract/interface type '{explicitType.FullName}'. Use a concrete implementation type.");
            }

            return explicitType;
        }

        private static TerminalOperation ResolveTerminalOperation(IReadOnlyList<PathSegment> segments)
        {
            var last = segments.LastOrDefault();
            if (last == null || !last.IsProperty || string.IsNullOrWhiteSpace(last.PropertyName))
                return TerminalOperation.Set;

            if (string.Equals(last.PropertyName, "Link", StringComparison.OrdinalIgnoreCase))
                return TerminalOperation.Link;
            if (string.Equals(last.PropertyName, "Unlink", StringComparison.OrdinalIgnoreCase))
                return TerminalOperation.Unlink;
            if (string.Equals(last.PropertyName, "Delete", StringComparison.OrdinalIgnoreCase))
                return TerminalOperation.Delete;

            return TerminalOperation.Set;
        }

        private static void ApplyPropertyTerminalOperation(
            object current,
            object? parent,
            PropertyInfo property,
            TerminalOperation operation,
            JToken value,
            FlowBloxRegistry registry,
            string path)
        {
            if (TryApplyScalarFieldSelection(current, property, operation, value, registry))
                return;

            if (operation == TerminalOperation.Set)
            {
                EnsureNoDirectReactiveObjectJsonAssignment(property.PropertyType, value, path);
                var convertedValue = ConvertToken(value, property.PropertyType, registry);
                EnsureReferenceValueIsSelectable(current, parent, property, convertedValue, path);
                property.SetValue(current, convertedValue);
                if (current is FlowBloxReactiveObject reactiveObject && FlowBloxFieldSelection.SupportsScalar(property.PropertyType))
                    reactiveObject.SetPropertySelectedField(property.Name, null);
                return;
            }

            if (operation == TerminalOperation.Delete)
            {
                DeletePropertyValue(current, property, path);
                return;
            }

            ApplyReferenceLinkOperationToProperty(current, parent, property, operation, value, registry, path);
        }

        private static bool TryApplyScalarFieldSelection(
            object target,
            PropertyInfo property,
            TerminalOperation operation,
            JToken value,
            FlowBloxRegistry registry)
        {
            var reactiveObject = target as FlowBloxReactiveObject;
            if (reactiveObject == null || !FlowBloxFieldSelection.Supports(property))
                return false;

            var isString = property.PropertyType == typeof(string);
            if (operation == TerminalOperation.Unlink && !isString)
            {
                reactiveObject.SetPropertySelectedField(property.Name, null);
                return true;
            }

            if (operation != TerminalOperation.Link)
                return false;

            var explicitReference = (value as JObject)?.Value<string>("resolveFieldElementByFQName");
            var fieldName = explicitReference;
            if (fieldName == null && !isString && value.Type == JTokenType.String)
            {
                var text = value.Value<string>();
                if (!string.IsNullOrWhiteSpace(text) && FieldElement.IsFullyQualifiedName(text))
                    fieldName = text;
            }

            if (fieldName == null)
                return false;

            if (explicitReference != null && ResolveFieldElementByFQName(registry, fieldName) == null)
                throw new InvalidOperationException($"Field reference '{fieldName}' could not be resolved.");

            if (isString)
                property.SetValue(target, fieldName);
            else
                reactiveObject.SetPropertySelectedFieldName(property.Name, fieldName);

            FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
            return true;
        }

        private static void ApplyIndexedTerminalOperation(
            IList list,
            Type elementType,
            Type? explicitElementType,
            int index,
            TerminalOperation operation,
            JToken value,
            FlowBloxRegistry registry,
            string path,
            object? collectionOwner)
        {
            if (operation == TerminalOperation.Delete)
            {
                DeleteIndexedValue(list, elementType, index, path);
                return;
            }

            if (operation == TerminalOperation.Unlink)
            {
                if (!IsReferenceComponentType(elementType))
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' unlink is only supported for FlowBlock/ManagedObject/FieldElement references. " +
                        "For FlowBloxReactiveObject-only (non-reference component) values use '/.../Delete'.");
                }

                UnlinkIndexedValue(list, index);
                return;
            }

            EnsureListCountForIndexAssignment(list, index, elementType);
            var existingValue = list[index];
            if (explicitElementType != null
                && existingValue != null
                && existingValue.GetType() != explicitElementType)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' typed index '{index}:{explicitElementType.FullName}' resolved existing type '{existingValue.GetType().FullName}'. " +
                    "Delete this index first via '/.../<index>/Delete', then create the desired type.");
            }

            if (operation == TerminalOperation.Link)
            {
                var resolvedReference = ResolveReferenceForTargetType(value, elementType, registry, path);
                if (resolvedReference == null)
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' link operation requires a resolvable reference value.");
                }

                list[index] = resolvedReference;
                return;
            }

            EnsureNoDirectReactiveObjectJsonAssignment(elementType, value, path);
            var convertedValue = ConvertToken(value, elementType, registry);
            if (explicitElementType != null && convertedValue != null && convertedValue.GetType() != explicitElementType)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' typed index expects '{explicitElementType.FullName}', but value resolved to '{convertedValue.GetType().FullName}'.");
            }

            list[index] = convertedValue;
        }

        private static void DeletePropertyValue(object current, PropertyInfo property, string path)
        {
            var nonNullableType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var elementType = GetElementType(nonNullableType);
            var actualType = elementType ?? nonNullableType;

            if (elementType != null && property.GetValue(current) is IList listValue)
            {
                if (!FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(actualType))
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' delete operation is only supported for FlowBloxReactiveObject-only (non-reference component) values. " +
                        "Use /.../Unlink for ManagedObject/FieldElement/FlowBlock references. " +
                        $"{GetDirectDeleteToolHint(actualType)}");
                }

                var elements = listValue.Cast<object?>()
                    .Where(x => x != null)
                    .Cast<object>()
                    .Distinct(ReferenceEqualityComparer.Instance)
                    .ToList();

                ExecuteReactiveDeletion(elements, path);
                return;
            }

            if (!FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(actualType))
            {
                throw new InvalidOperationException(
                    $"Path '{path}' delete operation is only supported for FlowBloxReactiveObject-only (non-reference component) values. " +
                    "Use DeleteFlowBlock/DeleteManagedObject for permanent component deletion or /.../Unlink for references. " +
                    $"{GetDirectDeleteToolHint(actualType)}");
            }

            var propertyValue = property.GetValue(current);
            if (propertyValue != null)
                ExecuteReactiveDeletion([propertyValue], path);
        }

        private static void DeleteIndexedValue(IList list, Type elementType, int index, string path)
        {
            if (!FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(elementType))
            {
                throw new InvalidOperationException(
                    $"Path '{path}' delete operation targets '{elementType.FullName}'. " +
                    "Only FlowBloxReactiveObject-only (non-reference component) list elements can be deleted here. " +
                    "For references use /.../Unlink. For permanent deletion use DeleteFlowBlock/DeleteManagedObject. " +
                    $"{GetDirectDeleteToolHint(elementType)}");
            }

            if (index >= 0 && index < list.Count)
            {
                var item = list[index];
                if (item != null)
                    ExecuteReactiveDeletion([item], path);
            }
        }

        private static void ExecuteReactiveDeletion(IReadOnlyCollection<object> instances, string path)
        {
            var registry = GetRegistry();
            var report = FlowBloxDeletionHelper.Analyze(registry, instances);
            var action = FlowBloxDeletionHelper.CreateAction(registry, report);
            action.Invoke();
            RegisterDeletionHistory(action);
            CurrentDeletionReports.Value?.Add(report);
        }

        private static JArray BuildCombinedDeletionReport(IEnumerable<DeletionReport>? reports)
            => reports == null
                ? new JArray()
                : new JArray(reports.SelectMany(x => BuildDeletionReport(x)));

        private static void AddCombinedDeletionReportIfAny(
            JObject result,
            IEnumerable<DeletionReport>? reports)
        {
            var deletionReport = BuildCombinedDeletionReport(reports);
            if (deletionReport.Count > 0)
                result["deletionReport"] = deletionReport;
        }

        private static void UnlinkIndexedValue(IList list, int index)
        {
            if (index >= 0 && index < list.Count)
                list.RemoveAt(index);
        }

        private static void EnsureListCountForIndexAssignment(IList list, int index, Type elementType)
        {
            while (list.Count <= index)
            {
                list.Add(elementType.IsValueType ? Activator.CreateInstance(elementType) : null);
            }
        }

        private static void ApplyReferenceLinkOperationToProperty(
            object current,
            object? parent,
            PropertyInfo property,
            TerminalOperation operation,
            JToken value,
            FlowBloxRegistry registry,
            string path)
        {
            var propertyType = property.PropertyType;
            var elementType = GetElementType(propertyType);
            if (elementType != null)
            {
                if (!IsReferenceComponentType(elementType))
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' link/unlink is only supported for FlowBlock/ManagedObject/FieldElement reference collections.");
                }

                var collection = property.GetValue(current) as IList;
                if (collection == null)
                {
                    collection = CreateInstance(propertyType) as IList
                                 ?? throw new InvalidOperationException($"Path '{path}' targets non-list collection type '{propertyType.FullName}'.");
                    property.SetValue(current, collection);
                }

                if (operation == TerminalOperation.Unlink)
                {
                    var toUnlink = ResolveReferenceForTargetType(value, elementType, registry, path, allowNull: true);
                    if (toUnlink == null)
                        return;

                    for (var i = collection.Count - 1; i >= 0; i--)
                    {
                        if (ReferenceEquals(collection[i], toUnlink))
                            collection.RemoveAt(i);
                    }

                    return;
                }

                if (operation == TerminalOperation.Link)
                {
                    var toLink = ResolveReferenceForTargetType(value, elementType, registry, path);
                    if (toLink == null)
                    {
                        throw new InvalidOperationException(
                            $"Path '{path}' link operation requires a resolvable reference value.");
                    }

                    EnsureReferenceValueIsSelectable(current, parent, property, toLink, path);

                    for (var i = 0; i < collection.Count; i++)
                    {
                        if (ReferenceEquals(collection[i], toLink))
                            return;
                    }

                    collection.Add(toLink);
                    return;
                }

                throw new InvalidOperationException(
                    $"Path '{path}' supports only Link/Unlink operations for reference collections.");
            }

            var nonNullablePropertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (!IsReferenceComponentType(nonNullablePropertyType))
            {
                throw new InvalidOperationException(
                    $"Path '{path}' link/unlink is only supported for FlowBlock/ManagedObject/FieldElement references.");
            }

            if (operation == TerminalOperation.Unlink)
            {
                // Optional resolver-supported unlink for single references:
                // - no value: unconditional unlink
                // - with resolver: unlink only if current reference matches
                if (value.Type != JTokenType.Null)
                {
                    var toUnlink = ResolveReferenceForTargetType(value, propertyType, registry, path, allowNull: true);
                    if (toUnlink == null)
                        return;

                    var currentValue = property.GetValue(current);
                    if (currentValue == null || !ReferenceEquals(currentValue, toUnlink))
                        return;
                }

                property.SetValue(current, null);
                return;
            }

            if (operation == TerminalOperation.Link)
            {
                var resolved = ResolveReferenceForTargetType(value, propertyType, registry, path);
                if (resolved == null)
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' link operation requires a resolvable reference value.");
                }

                EnsureReferenceValueIsSelectable(current, parent, property, resolved, path);
                property.SetValue(current, resolved);
                return;
            }

            throw new InvalidOperationException($"Path '{path}' uses unsupported operation '{operation}'.");
        }

        private static object? ResolveReferenceForTargetType(
            JToken token,
            Type targetType,
            FlowBloxRegistry registry,
            string path,
            bool allowNull = false)
        {
            var nonNullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (typeof(BaseFlowBlock).IsAssignableFrom(nonNullableTargetType))
            {
                var resolvedFlowBlock = ResolveFlowBlockReference(token, registry);
                if (resolvedFlowBlock == null && !allowNull)
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' expected flow block reference. Use \"FlowBlockName\" or {{\"resolveFlowBlockByName\":\"FlowBlockName\"}}.");
                }

                return resolvedFlowBlock;
            }

            if (typeof(IManagedObject).IsAssignableFrom(nonNullableTargetType))
            {
                var resolvedManagedObject = ResolveManagedObjectReference(token, registry);
                if (resolvedManagedObject == null && !allowNull)
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' expected managed object reference. Use {{\"resolveManagedObjectByName\":\"...\"}} or {{\"resolveFieldElementByFQName\":\"$FlowBlock::FieldName\"}}.");
                }

                if (resolvedManagedObject != null && !nonNullableTargetType.IsAssignableFrom(resolvedManagedObject.GetType()))
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' resolved reference type '{resolvedManagedObject.GetType().FullName}' is not assignable to '{nonNullableTargetType.FullName}'.");
                }

                return resolvedManagedObject;
            }

            throw new InvalidOperationException(
                $"Path '{path}' link/unlink is only supported for FlowBlock/ManagedObject/FieldElement references and their collections.");
        }

        private static bool IsReferenceComponentType(Type type)
        {
            return typeof(IManagedObject).IsAssignableFrom(type) || 
                   typeof(BaseFlowBlock).IsAssignableFrom(type);
        }

        private static string GetReferenceComponentLabel(Type type)
        {
            if (typeof(BaseFlowBlock).IsAssignableFrom(type))
                return "flow block reference";

            if (typeof(IManagedObject).IsAssignableFrom(type))
                return "managed object reference";

            return "component reference";
        }

        private static string GetDirectUpdateToolHint(Type type)
        {
            if (typeof(BaseFlowBlock).IsAssignableFrom(type))
                return "Update the referenced flow block directly via UpdateFlowBlock.";

            if (typeof(IManagedObject).IsAssignableFrom(type))
                return "Update the referenced managed object directly via UpdateManagedObject.";

            return "Update the referenced component directly via its dedicated update tool (UpdateFlowBlock or UpdateManagedObject).";
        }

        private static string GetDirectDeleteToolHint(Type type)
        {
            if (typeof(BaseFlowBlock).IsAssignableFrom(type))
                return "Delete the flow block via DeleteFlowBlock; dependencies are analyzed and handled automatically.";

            if (typeof(IManagedObject).IsAssignableFrom(type))
                return "Delete the managed object via DeleteManagedObject; dependencies are analyzed and handled automatically.";

            return "Delete reference components via DeleteFlowBlock/DeleteManagedObject; dependencies are analyzed and handled automatically.";
        }

        private static object? ConvertToken(JToken token, Type targetType, FlowBloxRegistry registry)
        {
            if (token.Type == JTokenType.Null)
            {
                return null;
            }

            var nonNullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (nonNullableTargetType == typeof(string))
            {
                return token.Value<string>();
            }

            if (nonNullableTargetType.IsEnum)
            {
                return ParseEnumValue(token, nonNullableTargetType);
            }

            if (typeof(BaseFlowBlock).IsAssignableFrom(nonNullableTargetType))
            {
                var resolvedFlowBlock = ResolveFlowBlockReference(token, registry);
                if (resolvedFlowBlock == null)
                {
                    throw new InvalidOperationException(
                        "Flow block reference could not be resolved. " +
                        "Use a flow block name string or {\"resolveFlowBlockByName\":\"FlowBlockName\"}.");
                }

                return resolvedFlowBlock;
            }

            if (typeof(IManagedObject).IsAssignableFrom(nonNullableTargetType))
            {
                var resolvedManagedObject = ResolveManagedObjectReference(token, registry);
                if (resolvedManagedObject == null)
                {
                    throw new InvalidOperationException(
                        "Managed object reference could not be resolved. " +
                        "Use {\"resolveManagedObjectByName\":\"...\"} or {\"resolveFieldElementByFQName\":\"$FlowBlock::FieldName\"}.");
                }

                if (!nonNullableTargetType.IsAssignableFrom(resolvedManagedObject.GetType()))
                {
                    throw new InvalidOperationException(
                        $"Resolved reference type '{resolvedManagedObject.GetType().FullName}' is not assignable to '{nonNullableTargetType.FullName}'.");
                }

                return resolvedManagedObject;
            }

            if (token.Type == JTokenType.Object && token is JObject objectToken)
            {
                var explicitTypeFullName = objectToken.Value<string>("$type")
                                           ?? objectToken.Value<string>("typeFullName");
                if (!string.IsNullOrWhiteSpace(explicitTypeFullName))
                {
                    var explicitType = ResolveType(explicitTypeFullName);
                    if (explicitType == null || !nonNullableTargetType.IsAssignableFrom(explicitType))
                    {
                        throw new InvalidOperationException(
                            $"Explicit type '{explicitTypeFullName}' is not assignable to '{nonNullableTargetType.FullName}'.");
                    }

                    return objectToken.ToObject(explicitType);
                }
            }

            return token.ToObject(targetType);
        }

        private static object ParseEnumValue(JToken token, Type enumType)
        {
            if (token.Type == JTokenType.Integer)
            {
                var underlyingType = Enum.GetUnderlyingType(enumType);
                var numericValue = Convert.ChangeType(token.ToObject(underlyingType), underlyingType);
                return Enum.ToObject(enumType, numericValue!);
            }

            if (token.Type == JTokenType.String)
            {
                var enumText = token.Value<string>() ?? string.Empty;
                return Enum.Parse(enumType, enumText, true);
            }

            if (token.Type == JTokenType.Object && token is JObject enumObject)
            {
                var enumText = enumObject.Value<string>("value")
                               ?? enumObject.Value<string>("name");
                if (!string.IsNullOrWhiteSpace(enumText))
                {
                    return Enum.Parse(enumType, enumText, true);
                }
            }

            throw new InvalidOperationException(
                $"Unsupported enum token '{token.Type}'. Use enum member name string, integer value, or {{\"value\":\"Member\"}}.");
        }

        private static IManagedObject? ResolveManagedObjectReference(JToken token, FlowBloxRegistry registry)
        {
            if (token.Type == JTokenType.Object && token is JObject objToken)
            {
                var managedObjectName = objToken.Value<string>("resolveManagedObjectByName");
                if (!string.IsNullOrWhiteSpace(managedObjectName))
                {
                    var resolvedManagedObject = ResolveManagedObjectByName(registry, managedObjectName);
                    if (resolvedManagedObject == null)
                    {
                        throw new InvalidOperationException(
                            $"Managed object could not be found by name \"{managedObjectName}\".");
                    }

                    return resolvedManagedObject;
                }

                var fieldFQName = objToken.Value<string>("resolveFieldElementByFQName");
                if (!string.IsNullOrWhiteSpace(fieldFQName))
                {
                    var resolvedFieldElement = ResolveFieldElementByFQName(registry, fieldFQName);
                    if (resolvedFieldElement == null)
                    {
                        throw new InvalidOperationException(
                            $"Field element could not be found by name \"{fieldFQName}\".");
                    }

                    return resolvedFieldElement;
                }
            }

            if (token.Type == JTokenType.String)
            {
                var raw = token.Value<string>();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                if (raw.StartsWith("$", StringComparison.Ordinal))
                {
                    return ResolveFieldElementByFQName(registry, raw);
                }

                return ResolveManagedObjectByName(registry, raw);
            }

            return null;
        }

        private static void EnsureReferenceValueIsSelectable(
            object target,
            object? parent,
            PropertyInfo property,
            object? value,
            string path)
        {
            if (value == null)
                return;

            var itemsSource = property.GetFlowBloxItemsSourceAttribute();
            if (string.IsNullOrWhiteSpace(itemsSource?.Method))
                return;

            if (!IsReferenceComponentType(value.GetType()))
                return;

            if (!TryResolveSelectionFilterMethod(target, parent, itemsSource.Method, out var invocationTarget, out var selectionMethod))
                return;

            var selectionResult = selectionMethod.Invoke(invocationTarget, null);
            if (selectionResult is not IEnumerable selectableValues)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' cannot validate property '{property.Name}' because selection filter method '{itemsSource.Method}' did not return a selectable list.");
            }

            var selectableList = selectableValues
                .Cast<object?>()
                .Where(x => x != null)
                .Cast<object>()
                .ToList();

            if (selectableList.Any(selectableValue => ReferenceEquals(selectableValue, value) || IsSameReferenceComponent(selectableValue, value)))
                return;

            throw new InvalidOperationException(
                BuildNotSelectableMessage(path, target, property, itemsSource.Method, value, selectableList));
        }

        private static bool TryResolveSelectionFilterMethod(
            object target,
            object? parent,
            string methodName,
            out object invocationTarget,
            out MethodInfo method)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            method = target.GetType().GetMethod(methodName, flags);
            if (method != null)
            {
                invocationTarget = target;
                return true;
            }

            if (parent != null)
            {
                method = parent.GetType().GetMethod(methodName, flags);
                if (method != null)
                {
                    invocationTarget = parent;
                    return true;
                }
            }

            invocationTarget = target;
            return false;
        }

        private static bool IsSameReferenceComponent(object selectableValue, object value)
        {
            if (selectableValue is FieldElement selectableField && value is FieldElement field)
            {
                return string.Equals(selectableField.FullyQualifiedName, field.FullyQualifiedName, StringComparison.OrdinalIgnoreCase);
            }

            if (selectableValue is BaseFlowBlock selectableFlowBlock && value is BaseFlowBlock flowBlock)
            {
                return string.Equals(selectableFlowBlock.Name, flowBlock.Name, StringComparison.OrdinalIgnoreCase);
            }

            if (selectableValue is IManagedObject selectableManagedObject && value is IManagedObject managedObject)
            {
                return string.Equals(selectableManagedObject.Name, managedObject.Name, StringComparison.OrdinalIgnoreCase);
            }

            return Equals(selectableValue, value);
        }

        private static string BuildNotSelectableMessage(
            string path,
            object target,
            PropertyInfo property,
            string selectionFilterMethod,
            object value,
            IReadOnlyCollection<object> selectableValues)
        {
            var selectedName = GetReferenceComponentName(value);
            var allowedNames = selectableValues
                .Select(GetReferenceComponentName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(12)
                .ToList();

            var allowedText = allowedNames.Count == 0
                ? "No values are currently selectable."
                : $"Selectable values are: {string.Join(", ", allowedNames)}{(selectableValues.Count > allowedNames.Count ? ", ..." : ".")}";
            var noSelectableValuesHint = allowedNames.Count == 0
                ? " Special hint: If no values are selectable, the flow block may not reference a BaseResultFlowBlock that can provide selectable values."
                : string.Empty;

            return
                $"Path '{path}' cannot set property '{property.Name}' on '{target.GetType().Name}' to '{selectedName}' because that value is not selectable by '{selectionFilterMethod}'. " +
                $"{allowedText} " +
                "Use one of the selectable values or change the flow design first. " + 
                "Special hint: For BasePipeFlowBlock input fields this usually means using a field from the directly associated predecessor flow block." +
                noSelectableValuesHint;
        }

        private static string GetReferenceComponentName(object value)
        {
            if (value is FieldElement fieldElement)
                return fieldElement.FullyQualifiedName;

            if (value is BaseFlowBlock flowBlock)
                return flowBlock.Name;

            if (value is IManagedObject managedObject)
                return managedObject.Name;

            return value.ToString() ?? value.GetType().Name;
        }

        private static BaseFlowBlock? ResolveFlowBlockReference(JToken token, FlowBloxRegistry registry)
        {
            if (token.Type == JTokenType.Object && token is JObject objToken)
            {
                var flowBlockName = objToken.Value<string>("resolveFlowBlockByName");
                return ResolveFlowBlockByName(registry, flowBlockName);
            }

            if (token.Type == JTokenType.String)
            {
                return ResolveFlowBlockByName(registry, token.Value<string>());
            }

            return null;
        }

        private static object CreateInstance(Type type, object? parentTarget = null)
        {
            if (type.IsInterface
                && type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(IList<>)
                    || type.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
            {
                var elementType = type.GetGenericArguments()[0];
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            }

            if (type.IsValueType)
            {
                return Activator.CreateInstance(type)!;
            }

            if (parentTarget != null)
            {
                var parentType = parentTarget.GetType();
                var matchingConstructor = type.GetConstructors()
                    .Select(x => new { Constructor = x, Parameters = x.GetParameters() })
                    .Where(x => x.Parameters.Length == 1 && x.Parameters[0].ParameterType.IsAssignableFrom(parentType))
                    .OrderByDescending(x => x.Parameters[0].ParameterType == parentType)
                    .FirstOrDefault();

                if (matchingConstructor != null)
                    return matchingConstructor.Constructor.Invoke([parentTarget]);
            }

            return Activator.CreateInstance(type)
                   ?? throw new InvalidOperationException($"Could not create instance of '{type.FullName}'.");
        }

        private static object CreateCollectionElementInstance(Type elementType, Type? explicitType = null, object? collectionOwner = null)
        {
            if (explicitType != null)
            {
                if (!elementType.IsAssignableFrom(explicitType))
                {
                    throw new InvalidOperationException(
                        $"Explicit element type '{explicitType.FullName}' is not assignable to '{elementType.FullName}'.");
                }

                if (explicitType.IsAbstract || explicitType.IsInterface)
                {
                    throw new InvalidOperationException(
                        $"Explicit element type '{explicitType.FullName}' cannot be abstract/interface.");
                }

                return CreateInstance(explicitType, collectionOwner);
            }

            if (!elementType.IsAbstract && !elementType.IsInterface)
            {
                return CreateInstance(elementType, collectionOwner);
            }

            if (FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(elementType))
            {
                var supportedTypes = GetSupportedTypeInfos(elementType)
                    .Select(x => x.Value<string>("fullName"))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (supportedTypes.Count == 1)
                {
                    var resolvedType = ResolveType(supportedTypes[0]!);
                    if (resolvedType != null)
                    {
                        return CreateInstance(resolvedType, collectionOwner);
                    }
                }

                throw new InvalidOperationException(
                    $"Collection element type '{elementType.FullName}' is abstract/interface. " +
                    "Use GetSupportedTypes for this base type and set the element explicitly with a typed object: " +
                    "{\"$type\":\"Full.Type.Name\", ...}.");
            }

            throw new InvalidOperationException(
                $"Collection element type '{elementType.FullName}' is abstract/interface and cannot be auto-created.");
        }

        private static void EnsureNoDirectReactiveObjectJsonAssignment(Type targetType, JToken value, string path)
        {
            var nonNullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var elementType = GetElementType(nonNullableTargetType);
            var valueTargetType = Nullable.GetUnderlyingType(elementType ?? nonNullableTargetType) ?? (elementType ?? nonNullableTargetType);

            if (!FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(valueTargetType))
                return;

            if (value.Type != JTokenType.Object && value.Type != JTokenType.Array)
                return;

            throw new InvalidOperationException(
                $"Path '{path}' targets a FlowBloxReactiveObject-only type ('{valueTargetType.FullName}'). " +
                "Do not assign JSON objects/arrays directly in 'value'. " +
                "Use path-based updates for sub-properties so Get-or-Create can materialize intermediate objects, " +
                "e.g. '/MappingEntries/0/ColumnName' and '/MappingEntries/0/Field' with " +
                "{\"resolveFieldElementByFQName\":\"$ReadExcelRows::Last Name\"}.");
        }

        private enum TerminalOperation
        {
            Set,
            Link,
            Unlink,
            Delete
        }
    }
}
