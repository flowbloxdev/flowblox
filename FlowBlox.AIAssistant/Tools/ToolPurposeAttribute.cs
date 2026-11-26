namespace FlowBlox.AIAssistant.Tools;

/// <summary>
/// Declares whether a tool's full contract is part of the bootstrap or listed by its compact purpose for on-demand loading.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ToolPurposeAttribute(string description) : Attribute
{
    public string Description { get; } = description;
    public bool IncludeContractInBootstrap { get; set; }
}
