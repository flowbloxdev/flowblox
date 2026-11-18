using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;

namespace FlowBlox.Core.Util.FlowBlocks;

/// <summary>
/// Defines selectable graph connections without following explicit recursive-call targets.
/// </summary>
internal static class FlowBlockConnectionRules
{
    public static List<BaseFlowBlock> GetPossibleNext(BaseFlowBlock source, IEnumerable<BaseFlowBlock> blocks)
    {
        if (!source.CanBeReferenced || source is NoteFlowBlock)
            return new();

        var ancestors = GetAncestors(source);
        return blocks.Where(target =>
                !ReferenceEquals(target, source) &&
                target is not NoteFlowBlock &&
                !ancestors.Contains(target) &&
                !target.ReferencedFlowBlocks.Contains(source) &&
                target.GetInputCardinality() != FlowBlockCardinalities.None &&
                (target.GetInputCardinality() != FlowBlockCardinalities.One || target.ReferencedFlowBlocks.Count == 0))
            .ToList();
    }

    public static List<BaseFlowBlock> GetPossibleIterationContexts(BaseFlowBlock target, IEnumerable<BaseFlowBlock> blocks)
    {
        if (target is StartFlowBlock or NoteFlowBlock)
            return new();

        var available = blocks.ToHashSet();
        var start = available.OfType<StartFlowBlock>().FirstOrDefault();
        if (start == null)
            return new();

        var ancestorsByBlock = new Dictionary<BaseFlowBlock, HashSet<BaseFlowBlock>>();
        var ancestors = GetAncestors(target);
        ancestorsByBlock[target] = ancestors;
        foreach (var ancestor in ancestors)
            ancestorsByBlock[ancestor] = GetAncestors(ancestor);

        var hidden = new HashSet<BaseFlowBlock>();
        foreach (var predecessor in ancestors)
        {
            var context = predecessor.IterationContext;
            if (context == null || !ancestorsByBlock[predecessor].Contains(context))
                continue;

            // Hide only the strict interior of the closed scope, not its producer or context boundary.
            foreach (var interior in ancestorsByBlock[predecessor])
            {
                if (interior != context && ancestorsByBlock.TryGetValue(interior, out var previous) && previous.Contains(context))
                    hidden.Add(interior);
            }
        }

        return ancestors
            .Where(candidate => available.Contains(candidate) && candidate != target && candidate is not NoteFlowBlock &&
                !target.ReferencedFlowBlocks.Contains(candidate) && !hidden.Contains(candidate) &&
                (candidate == start || ancestorsByBlock[candidate].Contains(start)))
            .OrderBy(candidate => candidate.Name)
            .ToList();
    }

    private static HashSet<BaseFlowBlock> GetAncestors(BaseFlowBlock block)
    {
        var result = new HashSet<BaseFlowBlock>();
        var pending = new Stack<BaseFlowBlock>(block.ReferencedFlowBlocks.Where(previous => previous != null));
        while (pending.TryPop(out var current))
        {
            if (current == block || !result.Add(current))
                continue;
            foreach (var previous in current.ReferencedFlowBlocks.Where(previous => previous != null))
                pending.Push(previous);
        }
        return result;
    }
}
