namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Marks a readable property as immutable through external update APIs, including child paths.
    /// Internal domain operations and serialization may still maintain its value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class FlowBloxImmutableAttribute : Attribute
    {
    }
}
