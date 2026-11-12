using System.Reflection;
using FlowBlox.Core.Attributes;

namespace FlowBlox.Core.Util.Fields
{
    /// <summary>
    /// Single source of truth for properties explicitly enabled for field references.
    /// Selection attributes restrict the offered fields, never the CLR type of their values.
    /// </summary>
    public static class FlowBloxFieldSelection
    {
        public static bool Supports(Type propertyType)
        {
            var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            return type == typeof(string)
                || SupportsScalar(type);
        }

        public static bool Supports(PropertyInfo property)
        {
            return property.CanRead
                && property.CanWrite
                && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<FlowBloxUIAttribute>(inherit: true)
                    ?.UiOptions.HasFlag(UIOptions.EnableFieldSelection) == true
                && Supports(property.PropertyType);
        }

        public static bool SupportsScalar(Type propertyType)
        {
            var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            return type.IsEnum
                || type == typeof(bool)
                || type == typeof(int)
                || type == typeof(long)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(DateTime);
        }
    }
}
