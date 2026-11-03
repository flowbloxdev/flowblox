using System.Reflection;

namespace FlowBlox.Core.Util
{
    public static class EnumHelper
    {
        /// <summary>
        /// Determines whether a property has an enum or nullable enum type.
        /// </summary>
        public static bool IsEnumType(PropertyInfo property)
        {
            var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            return underlyingType.IsEnum;
        }
    }
}
