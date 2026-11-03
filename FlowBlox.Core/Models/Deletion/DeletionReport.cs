namespace FlowBlox.Core.Models.Deletion
{
    public sealed class DeletionReport
    {
        public IReadOnlyList<DeletionDependency> Dependencies { get; init; } = Array.Empty<DeletionDependency>();
        public IReadOnlyList<object> Targets { get; init; } = Array.Empty<object>();
        public bool HasDependencies => Dependencies.Count > 0;
    }
}
