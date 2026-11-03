using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Attributes
{
    /// <summary>Places an enum value in a localized submenu in property editors.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FlowBloxEnumerationGroupAttribute : Attribute
    {
        public FlowBloxEnumerationGroupAttribute(string name) => Name = name;

        public string Name { get; }
        public Type? ResourceType { get; set; }

        public Type IconResourceType { get; set; } = typeof(FlowBloxIcons);
        public string IconResourceKey { get; set; } = string.Empty;
        public string IconColor { get; set; } = "#546E7A";

        public UIIconDefinition IconDefinition => new UIIconDefinition(IconResourceType, IconResourceKey, IconColor, 16);

        public string GetDisplayName() => ResourceType == null
            ? Name
            : FlowBloxResourceUtil.GetLocalizedString(Name, ResourceType) ?? Name;
    }
}
