using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Runtime;

namespace FlowBloxTest.Runtime;

public sealed class ResolutionProbeFlowBlock : StartFlowBlock
{
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public string Text { get; set; } = null!;

    public int Continuations { get; private set; }
    public bool ConsumedValue { get; private set; }

    public override void ExecuteNextFlowBlocks(BaseRuntime runtime) => Continuations++;

    public override bool Execute(BaseRuntime runtime, object data) => Invoke(runtime, data, () =>
    {
        if (!this.ResolveOrCreateNotification(
            x => x.Text,
            out var text,
            runtime,
            BaseFlowBlockNotifications.PreconditionsNotMet))
            return;

        ConsumedValue = true;
        ExecuteNextFlowBlocks(runtime);
    });
}
