namespace FlowBlox.Core.Attributes;

/// <summary>
/// Configures the header icon and optional row tooltip of an SKImage column in list views.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FlowBloxImageColumnAttribute : Attribute
{
    public FlowBloxImageColumnAttribute(Type iconResourceType, string iconResourceKey)
    {
        IconResourceType = iconResourceType;
        IconResourceKey = iconResourceKey;
    }

    public Type IconResourceType { get; }
    public string IconResourceKey { get; }
    public string IconColor { get; set; } = "#546E7A";
    public string ToolTipMemberName { get; set; } = string.Empty;

    public UIIconDefinition IconDefinition => new(IconResourceType, IconResourceKey, IconColor, 16);
}
