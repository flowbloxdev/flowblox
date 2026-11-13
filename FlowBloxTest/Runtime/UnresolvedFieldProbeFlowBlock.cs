using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Runtime;

namespace FlowBloxTest.Runtime;

public sealed class UnresolvedFieldProbeFlowBlock : StartFlowBlock
{
    public string Text { get; set; } = string.Empty;

    public Action? Observe { get; set; }

    public override bool Execute(BaseRuntime runtime, object data)
    {
        return Invoke(runtime, data, () =>
        {
            Observe?.Invoke();
            ExecuteNextFlowBlocks(runtime);
        });
    }
}
