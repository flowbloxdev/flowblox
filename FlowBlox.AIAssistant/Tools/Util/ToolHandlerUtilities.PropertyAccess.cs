using System.Reflection;
using FlowBlox.Core.Attributes;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static partial class ToolHandlerUtilities
    {
        /// <summary>
        /// Central assistant write policy for readable properties maintained by domain operations.
        /// Applies to inherited properties and every update path below the protected property.
        /// </summary>
        private static bool IsImmutableProperty(PropertyInfo property)
        {
            return property.GetCustomAttribute<FlowBloxImmutableAttribute>(inherit: true) != null;
        }

        private static void EnsurePropertyWriteAccess(PropertyInfo property)
        {
            if (IsImmutableProperty(property))
            {
                throw new InvalidOperationException(
                    $"Property '{property.Name}' is read-only to the AI assistant, including its child paths. " +
                    "Use the supported property update operations instead.");
            }
        }
    }
}
