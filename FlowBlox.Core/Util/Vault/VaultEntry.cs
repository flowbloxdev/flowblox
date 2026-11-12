using Newtonsoft.Json;

namespace FlowBlox.Core.Util.Vault;

/// <summary>
/// An editable local Vault entry. Never serialize this type into an AI response.
/// </summary>
public sealed class VaultEntry
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool IsPassword { get; set; }

    [JsonIgnore]
    public string Reference => "$Vault::" + Name;
}
