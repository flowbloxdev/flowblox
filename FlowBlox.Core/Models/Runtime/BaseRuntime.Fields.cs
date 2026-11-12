using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using System.Collections.Concurrent;

namespace FlowBlox.Core.Models.Runtime;

public abstract partial class BaseRuntime
{
    private readonly ConcurrentDictionary<BaseFlowBlock, FlowBloxReactiveObject[]> _fieldSelectionOwners = new();

    internal IDisposable ResolveSelectedFieldProperties(BaseFlowBlock block)
    {
        return new SelectedFieldPropertyScope(GetFieldSelectionOwners(block));
    }

    private FlowBloxReactiveObject[] GetFieldSelectionOwners(BaseFlowBlock block)
    {
        return _fieldSelectionOwners.GetOrAdd(block, current => current.GetAssociatedManagedObjects()
            .OfType<FlowBloxReactiveObject>()
            .Prepend(current)
            .SelectMany(owner => owner.GetNestedReactiveObjects().Prepend(owner))
            .Distinct<FlowBloxReactiveObject>(ReferenceEqualityComparer.Instance)
            .ToArray());
    }

}
