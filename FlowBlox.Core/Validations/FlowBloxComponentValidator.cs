using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.FlowBlocks;
using System.Collections;
using System.Reflection;
using FlowBlox.Core.Models.FlowBlocks.Base;

namespace FlowBlox.Core.Validations
{
    /// <summary>
    /// Validates a component and its owned reactive configuration objects without crossing
    /// component boundaries through referenced FlowBlocks or managed objects.
    /// </summary>
    public static class FlowBloxComponentValidator
    {
        public static List<string> Validate(FlowBloxReactiveObject root)
        {
            ArgumentNullException.ThrowIfNull(root);

            var messages = new List<string>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Validate(root, string.Empty, visited, messages);
            return messages;
        }

        private static void Validate(
            FlowBloxReactiveObject target,
            string path,
            HashSet<object> visited,
            List<string> messages)
        {
            if (!visited.Add(target))
                return;

            foreach (var error in target.ValidateObject().SelectMany(x => x.Value))
                messages.Add(string.IsNullOrWhiteSpace(path) ? error : $"[{path}] {error}");

            foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                if (typeof(FlowBloxReactiveObject).IsAssignableFrom(property.PropertyType))
                {
                    ValidateChild(property.GetValue(target), property.Name, path, visited, messages);
                    continue;
                }

                if (!ContainsReactiveObjects(property.PropertyType) ||
                    property.GetValue(target) is not IEnumerable collection)
                {
                    continue;
                }

                var index = 0;
                foreach (var item in collection)
                {
                    // An unbound input assignment represents no input and is
                    // removed by the next reference sync. Keep Required on the RO
                    // itself: the same RO may be meaningful in another context.
                    var assignment = item as InputBehaviorAssignment;
                    var isUnboundInput = target is BaseFlowBlock &&
                        property.Name == nameof(BaseFlowBlock.InputBehaviorAssignments) &&
                        assignment != null && assignment.FlowBlock == null;
                    if (!isUnboundInput)
                        ValidateChild(item, $"{property.Name}[{index}]", path, visited, messages);
                    index++;
                }
            }
        }

        private static bool ContainsReactiveObjects(Type collectionType)
        {
            var elementType = collectionType.IsArray
                ? collectionType.GetElementType()
                : collectionType
                    .GetInterfaces()
                    .Append(collectionType)
                    .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GetGenericArguments()[0];

            return elementType != null &&
                   typeof(FlowBloxReactiveObject).IsAssignableFrom(elementType);
        }

        private static void ValidateChild(
            object? child,
            string childPath,
            string parentPath,
            HashSet<object> visited,
            List<string> messages)
        {
            if (child is not FlowBloxReactiveObject reactiveChild ||
                !FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(reactiveChild))
                return;

            var path = string.IsNullOrWhiteSpace(parentPath)
                ? childPath
                : $"{parentPath}.{childPath}";
            Validate(reactiveChild, path, visited, messages);
        }
    }
}
