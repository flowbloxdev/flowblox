using System.Collections;
using System.Reflection;
using FlowBlox.Core.Attributes;

namespace FlowBlox.UICore.PropertyView.Resolver
{
    public static class SelectionItemsSourceResolver
    {
        public static IList ResolveItemsSource(
            SelectionMethodResolutionResult selectionMethodResolution,
            PropertyInfo property,
            object target = null)
        {
            if (selectionMethodResolution?.Method == null)
                return null;

            var originalItems = selectionMethodResolution.Method.Invoke(selectionMethodResolution.InvocationTarget, null) as IEnumerable;
            var items = new ArrayList();
            var nullable = !property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) != null;
            if (nullable && !IsRequired(property, target ?? selectionMethodResolution.InvocationTarget))
                items.Add(null);

            foreach (var item in originalItems ?? Array.Empty<object>())
            {
                if (item != null)
                    items.Add(item);
            }

            return items;
        }

        public static bool IsRequired(PropertyInfo property, object target)
        {
            if (property.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() != null)
                return true;

            var condition = property.GetCustomAttribute<RequiredIfAttribute>();
            return condition != null && target != null
                && Equals(target.GetType().GetProperty(condition.MemberName)?.GetValue(target), condition.Value);
        }
    }
}
