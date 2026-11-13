using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Runtime;

namespace FlowBloxTest.Runtime;

public sealed class ScalarFieldProbeFlowBlock : StartFlowBlock
{
    [Range(1, 100)]
    public int Count { get; set; } = 7;

    public bool Enabled { get; set; }

    public DateTime? Since { get; set; }

    public DotNetEncodingNames Encoding { get; set; } = DotNetEncodingNames.ASCII;
    public ScalarFieldProbeSettings Settings { get; set; } = new();
    public Action<ScalarFieldProbeFlowBlock>? Observe { get; set; }

    public override bool Execute(BaseRuntime runtime, object data)
    {
        return Invoke(runtime, data, () =>
        {
            Observe?.Invoke(this);
            ExecuteNextFlowBlocks(runtime);
        });
    }
}
