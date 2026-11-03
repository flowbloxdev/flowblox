using System.Reflection;
using FlowBlox.Enums;

namespace FlowBlox.Core.Models.Deletion;

public sealed class DeletionDependency
{
    public required object ReferencingObject { get; init; }
    public required PropertyInfo Property { get; init; }
    /// <summary>
    /// The top-level property on EditTarget through which this dependency is reached.
    /// </summary>
    public PropertyInfo ComponentProperty { get; init; }
    public required object ReferencedObject { get; init; }
    public required DeletionImpact Impact { get; init; }
    public required DeletionReviewStatus ReviewStatus { get; init; }
    public bool AutomaticallyRemoved => Impact != DeletionImpact.FieldReferenceInvalidated;
    public object EditTarget { get; init; }
    public int CollectionIndex { get; init; } = -1;
}
