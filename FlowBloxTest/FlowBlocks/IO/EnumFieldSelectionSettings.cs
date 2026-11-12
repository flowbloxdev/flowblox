using FlowBlox.Core.Enums;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;

namespace FlowBloxTest.FlowBlocks.IO;

public sealed class EnumFieldSelectionSettings : FlowBloxReactiveObject
{
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public DotNetEncodingNames Encoding { get; set; } = DotNetEncodingNames.ASCII;
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public SpecialSeparator? Separator { get; set; } = SpecialSeparator.NewLine;
}
