using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Deletion;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.FlowBlocks;
using System.Collections;
using System.Reflection;

namespace FlowBlox.Core.Util.Deletion
{
    internal static class DeletionDependencyGraphVisitor
    {
        internal static void VisitPropertiesRecursive(
            IEnumerable<object> roots,
            Action<DeletionDependencyVisitContext> visitor)
        {
            if (roots == null)
                throw new ArgumentNullException(nameof(roots));
            if (visitor == null)
                throw new ArgumentNullException(nameof(visitor));

            foreach (var root in roots.Where(x => x != null))
                VisitPropertiesRecursive(root, root, null, visitor, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static void VisitPropertiesRecursive(
            object root,
            object instance,
            PropertyInfo componentProperty,
            Action<DeletionDependencyVisitContext> visitor,
            ISet<object> visited)
        {
            if (!visited.Add(instance))
                return;

            var properties = instance.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.CanRead && x.CanWrite && x.GetIndexParameters().Length == 0)
                .Where(x => x.GetCustomAttribute<DeepCopierIgnoreAttribute>() == null);

            foreach (var property in properties)
            {
                object value;
                try
                {
                    value = property.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                var selectionOwner = instance as FlowBloxReactiveObject;
                if (value == null && selectionOwner?.GetPropertySelectedFieldName(property.Name) == null)
                    continue;

                var rootProperty = componentProperty ?? property;
                visitor(new DeletionDependencyVisitContext
                {
                    Root = root,
                    Owner = instance,
                    Property = property,
                    Value = value,
                    ComponentProperty = rootProperty
                });

                if (value is FlowBloxReactiveObject nested &&
                    FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(nested))
                {
                    VisitPropertiesRecursive(root, nested, rootProperty, visitor, visited);
                    continue;
                }

                if (value is IEnumerable enumerable && value is not string)
                {
                    foreach (var item in enumerable
                                 .OfType<FlowBloxReactiveObject>()
                                 .Where(FlowBloxReactiveObjectHelper.IsReactiveObjectOnly))
                    {
                        VisitPropertiesRecursive(root, item, rootProperty, visitor, visited);
                    }
                }
            }
            // Track the active path, not a global seen set: a shared reactive
            // object can have different review rules in different component slots.
            visited.Remove(instance);
        }
    }
}
