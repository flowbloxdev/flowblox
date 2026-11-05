namespace FlowBlox.Core.Util
{
    public static class PropertyTypeHelper
    {
        public static bool IsSimpleType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(Guid)
                || type == typeof(TimeSpan);
        }
    }
}
