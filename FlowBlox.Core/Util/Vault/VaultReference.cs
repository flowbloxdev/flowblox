namespace FlowBlox.Core.Util.Vault;

/// <summary>
/// Discoverable Vault metadata; intentionally carries no stored value.
/// </summary>
public sealed class VaultReference
{
    public VaultReference(
        string name,
        string description,
        bool isPassword,
        string reference)
    {
        Name = name;
        Description = description;
        IsPassword = isPassword;
        Reference = reference;
    }

    public string Name { get; }

    public string Description { get; }

    public bool IsPassword { get; }

    public string Reference { get; }
}
