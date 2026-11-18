using FlowBlox.Core.Models.FlowBlocks.Base;

namespace FlowBlox.Core.Models.Runtime.WorkItems;

/// <summary>
/// Reads buffered inputs after preceding iteration-end work has produced its downstream results.
/// </summary>
internal sealed class ProcessCollectedInputsWorkItem : IRuntimeWorkItem
{
    private readonly BaseFlowBlock _block;

    public ProcessCollectedInputsWorkItem(BaseFlowBlock block) => _block = block;

    public void Run(BaseRuntime runtime) => _block.ProcessCollectedInputs(runtime);
}
