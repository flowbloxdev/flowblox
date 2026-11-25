namespace FlowBlox.AIAssistant.Tools;

/// <summary>
/// Supplies the compact bootstrap catalog entry without changing the full tool contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ToolPurposeAttribute(string description) : Attribute
{
    public string Description { get; } = description;
    public bool IncludeContractInBootstrap { get; set; }
}
