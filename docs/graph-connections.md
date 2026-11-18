# Selecting flow connections

- `BaseFlowBlock.GetPossibleNextFlowBlocks()` supplies new normal successor connections. It excludes self-connections,
  ancestors that would close a cycle, existing edges, notes, unsupported input cardinalities and occupied single inputs.
  Acyclic forward shortcuts remain valid. Explicit recursive calls retain `GetPossibleTargetFlowBlocks()`.
- `GetPossibleReferencedElements()` derives predecessor choices from that successor method. Current associations remain
  available for unlinking, including when the input cardinality is full.
- `GetPossibleIterationContexts()` returns registered, Start-reachable upstream blocks, excluding direct predecessors.
  When an upstream block collects an iteration context, the strict interior between that context and the collecting
  block is hidden. Both boundaries remain eligible unless another rule excludes them. Automatically resolved contexts
  follow the same rule; an interior block is still hidden if another branch also reaches it.

The property dropdown and canvas preview use these methods directly. In **Iteration context** mode, drag from the block
that needs a context to its intended upstream context block. Invalid targets are dimmed with a red deny overlay, and the
preview cursor/line also indicate an invalid drop. Drop validation checks the current graph again before changing it.
Escape, capture loss, mode changes and runtime editing restrictions cancel the preview.

Explicit context links use the normal property-change action for undo/redo and can be removed through their context
arrow. Removing an explicit link restores automatic context resolution where applicable. These selection/UI changes do
not change runtime scheduling or the existing model safety checks.
