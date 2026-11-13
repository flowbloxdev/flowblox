using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Attributes;

namespace FlowBloxTest.Runtime;

public sealed class ScalarFieldProbeSettings : FlowBloxReactiveObject
{
    public int Limit { get; set; } = 10;

    [FlowBloxUI(UISelectionButtons.None)]
    public int LiteralOnly { get; set; } = 15;
}
