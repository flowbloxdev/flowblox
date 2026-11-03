namespace FlowBlox.Enums;

/// <summary>
/// Describes how deleting a target affects a reference to that target.
/// </summary>
public enum DeletionImpact
{
    /// <summary>
    /// A reference property is automatically set to null.
    /// </summary>
    DirectReferenceRemoved,
    /// <summary>
    /// A referenced item is automatically removed from a collection.
    /// </summary>
    CollectionEntryRemoved,
    /// <summary>
    /// A textual field placeholder remains but no longer resolves.
    /// </summary>
    FieldReferenceInvalidated
}
