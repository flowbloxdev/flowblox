using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Attributes;

namespace FlowBloxTest.Runtime;

public sealed class ScalarFieldProbeSettings : FlowBloxReactiveObject
{
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public int Limit { get; set; } = 10;

    public int LiteralOnly { get; set; } = 15;
}
