using System.Reflection;

namespace FlowBlox.Core.Models.Deletion;

/// <summary>
/// Describes a visited property while discovering deletion dependencies.
/// </summary>
internal sealed class DeletionDependencyVisitContext
{
    /// <summary>
    /// The root component whose configuration is being inspected.
    /// </summary>
    public required object Root { get; init; }

    /// <summary>
    /// The object declaring the visited property, possibly a nested reactive object.
    /// </summary>
    public required object Owner { get; init; }

    public required PropertyInfo Property { get; init; }

    public required object Value { get; init; }

    /// <summary>
    /// The top-level property on the root component through which the owner is reached.
    /// </summary>
    public required PropertyInfo ComponentProperty { get; init; }
}
