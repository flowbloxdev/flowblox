using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBloxTest.Runtime;

namespace FlowBloxTest.AIAssistant;

public sealed class ImmutablePropertyFixture : FlowBloxReactiveObject
{
    [FlowBloxImmutable]
    public string FixedValue { get; set; } = "Original";

    [FlowBloxImmutable]
    public ScalarFieldProbeSettings FixedSettings { get; set; } = new();
}
