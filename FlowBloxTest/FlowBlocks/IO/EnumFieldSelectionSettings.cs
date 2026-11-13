using FlowBlox.Core.Enums;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;

namespace FlowBloxTest.FlowBlocks.IO;

public sealed class EnumFieldSelectionSettings : FlowBloxReactiveObject
{
    public DotNetEncodingNames Encoding { get; set; } = DotNetEncodingNames.ASCII;

    public SpecialSeparator? Separator { get; set; } = SpecialSeparator.NewLine;
}
