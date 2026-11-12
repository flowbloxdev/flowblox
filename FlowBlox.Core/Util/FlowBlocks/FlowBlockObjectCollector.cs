using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using System.Collections;
using System.Reflection;

namespace FlowBlox.Core.Util.FlowBlocks
{
    internal class FlowBlockObjectCollector
    {
        private static bool IsIgnored(object parentObj, string propertyName)
        {
            if (parentObj is BaseFlowBlock flowBlock)
            {
                if (propertyName == nameof(BaseFlowBlock.Parent))
                    return true;

                if (propertyName == nameof(BaseFlowBlock.ReferencedFlowBlocks))
                    return true;
            }

            if (parentObj is FlowBloxComponent component)
            {
                if (propertyName == nameof(FlowBloxComponent.RequiredFields))
                    return true;
            }

            return false;
        }

        private static bool IsIgnored(object parentObj, object obj, Type[] excludedTypes)
        {
            if (parentObj != null)
            {
                if (excludedTypes != null && 
                    excludedTypes.Any(x => x.IsAssignableFrom(obj.GetType())))
                {
                    return true;
                }
            }

            if (parentObj is BaseFlowBlock flowBlock && obj is ManagedObject managedObject)
            {
                return flowBlock.DefinedManagedObjects != null &&
                       flowBlock.DefinedManagedObjects.Contains(managedObject);
            }

            return false;
        }

        public static IEnumerable<T> CollectObjects<T>(
            object obj, 
            HashSet<object> visited, 
            bool recursive = true, 
            object parentObj = null,
            Type[] excludedTypes = null,
            Action<object, IEnumerable<PropertyInfo>, List<T>> customNavigator = null)
        {
            if (obj == null || visited.Contains(obj))
                return Enumerable.Empty<T>();

            visited.Add(obj);

            var result = new List<T>();

            if (IsIgnored(parentObj, obj, excludedTypes))
                return result;

            if (obj is T foundObject)
                result.Add(foundObject);

            if (parentObj != null && !recursive)
                return result;

            var type = obj.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(prop => prop.CanWrite)
                .Where(prop => prop.GetCustomAttribute<DeepCopierIgnoreAttribute>() == null);

            // Custom logic for referenced components (e.g., string to FieldElements)
            customNavigator?.Invoke(obj, properties, result);

            // To-One Navigation
            var toOneProperties = properties
                .Where(p => typeof(T).IsAssignableFrom(p.PropertyType));

            foreach (var prop in toOneProperties.Where(x => !IsIgnored(obj, x.Name)))
            {
                if (prop.TryGetValue(obj, out var propValue) && propValue != null)
                {
                    result.AddRange(CollectObjects<T>(propValue, visited, recursive, obj, excludedTypes, customNavigator));
                }
            }

            // To-Many Navigation
            var toManyProperties = properties
                .Where(p =>
                    p.PropertyType.IsGenericType &&
                    typeof(IList).IsAssignableFrom(p.PropertyType) &&
                    typeof(T).IsAssignableFrom(p.PropertyType.GetGenericArguments().First())
                );

            foreach (var prop in toManyProperties.Where( x => !IsIgnored(obj, x.Name)))
            {
                if (!prop.TryGetValue(obj, out var value))
                    continue;

                if (value is not IList collection)
                    continue;

                foreach (var item in collection)
                {
                    if (item is T child)
                    {
                        result.AddRange(CollectObjects<T>(child, visited, recursive, obj, excludedTypes, customNavigator));
                    }
                }
            }

            return result;
        }

        public static IEnumerable<IManagedObject> CollectManagedObjectsRecursive(object root)
        {
            var managedObjects = CollectObjects<IManagedObject>(root, new HashSet<object>());
            if (root is IManagedObject rootManagedObject)
                managedObjects = managedObjects.Except([rootManagedObject]);
            return managedObjects;
        }

        public static IEnumerable<BaseFlowBlock> CollectFlowBlocks(object root)
        {
            var flowBlocks = CollectObjects<BaseFlowBlock>(root, new HashSet<object>(), recursive: false);
            if (root is BaseFlowBlock rootFlowBlock)
                flowBlocks = flowBlocks.Except([rootFlowBlock]);
            return flowBlocks;
        }

        public static IEnumerable<RequiredFieldContext> CollectRequiredFieldContextsRecursive(object root)
        {
            var allManagedObjects = CollectManagedObjectsRecursive(root);

            var result = new List<RequiredFieldContext>();
            foreach (var mo in allManagedObjects.Where(x => x.HandleRequirements))
            {
                result.AddRange(mo.RequiredFields.Select(x => new RequiredFieldContext()
                {
                    FieldElement = x,
                    FlowBloxComponent = (ManagedObject)mo
                }) ?? Enumerable.Empty<RequiredFieldContext>());
            }
            return result;
        }

        public static IEnumerable<FlowBloxReactiveObject> CollectNestedReactiveObjectsRecursive(object root)
        {
            return CollectObjects<FlowBloxReactiveObject>(
                root,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                excludedTypes: [typeof(FlowBloxComponent)])
                .Where(x => !ReferenceEquals(x, root) &&
                            FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(x));
        }

        public static IEnumerable<FieldElement> CollectReferencedFieldElementsRecursive(object root)
        {
            var reactiveObjects = CollectObjects<FlowBloxReactiveObject>(
                root,
                new HashSet<object>(),
                excludedTypes: [typeof(FlowBloxComponent)],
                customNavigator: NavigateReferencedFieldsFromString);

            if (root is FieldElement rootFieldElement)
                reactiveObjects = reactiveObjects.Except([rootFieldElement]);

            return reactiveObjects
                .OfType<FieldElement>()
                .Distinct();
        }

        private static void NavigateReferencedFieldsFromString(object obj, IEnumerable<PropertyInfo> props, List<FlowBloxReactiveObject> result)
        {
            if (obj is FlowBloxReactiveObject reactiveObject)
            {
                foreach (var propertyName in reactiveObject.PropertySelectedFieldMap.Keys)
                {
                    var property = obj.GetType().GetProperty(propertyName);
                    if (property == null || !FlowBloxFieldSelection.Supports(property))
                        continue;

                    var selectedField = reactiveObject.GetPropertySelectedField(propertyName);
                    if (selectedField != null)
                        result.Add(selectedField);
                }
            }

            foreach (var prop in props)
            {
                if (IsIgnored(obj, prop.Name))
                    continue;

                if (!prop.TryGetValue(obj, out var propValue) || propValue == null)
                    continue;

                if (propValue is FieldElement fieldElement)
                {
                    result.Add(fieldElement);
                    continue;
                }

                if (propValue is IEnumerable<FieldElement> fieldList)
                {
                    result.AddRange(fieldList);
                    continue;
                }

                if (FlowBloxFieldSelection.Supports(prop) &&
                    propValue is string strValue)
                {
                    result.AddRange(FlowBloxFieldHelper.GetFieldElementsFromString(strValue));
                }
            }
        }

        public static IEnumerable<(PropertyInfo Property, object Instance)> CollectStringPropertiesContainingFields(object root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            var matches = new HashSet<(PropertyInfo Property, object Instance)>();

            _ = CollectObjects<FlowBloxReactiveObject>(
                root,
                new HashSet<object>(),
                excludedTypes: [typeof(FlowBloxComponent)],
                customNavigator: (obj, props, _) => NavigateFieldSelectionStringProperties(obj, props, matches)
            );

            return matches;
        }

        private static void NavigateFieldSelectionStringProperties(
            object obj,
            IEnumerable<PropertyInfo> props,
            HashSet<(PropertyInfo Property, object Instance)> matches)
        {
            foreach (var prop in props)
            {
                if (IsIgnored(obj, prop.Name))
                    continue;

                if (prop.PropertyType != typeof(string) || !prop.CanWrite)
                    continue;

                if (!prop.TryGetValue(obj, out var propValue) || propValue is not string strValue)
                    continue;

                if (!FlowBloxFieldSelection.Supports(prop))
                    continue;

                matches.Add((prop, obj));
            }
        }
    }
}
