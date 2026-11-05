using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.Fields;

namespace FlowBloxTest.FlowBlocks.IO;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class EnumFieldSelectionTests
{
    public sealed class EnumSettings
    {
        public DotNetEncodingNames Encoding { get; set; } = DotNetEncodingNames.ASCII;
        public FieldElement? Encoding_SelectedField { get; set; }
        public SpecialSeparator? Separator { get; set; } = SpecialSeparator.NewLine;
        public FieldElement? Separator_SelectedField { get; set; }
    }

    [TestMethod]
    [DataRow("UTF8", DotNetEncodingNames.UTF8)]
    [DataRow("utf16", DotNetEncodingNames.UTF16)]
    [DataRow("invalid", DotNetEncodingNames.Default)]
    [DataRow("", DotNetEncodingNames.Default)]
    [DataRow(null, DotNetEncodingNames.Default)]
    public void DynamicEnum_ParsesOrUsesDefault(string? value, DotNetEncodingNames expected)
    {
        var settings = new EnumSettings
        {
            Encoding_SelectedField = new FieldElement(FieldTypes.Text) { StringValue = value }
        };

        Assert.AreEqual(expected, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Encoding));
        Assert.AreSame(settings.Encoding_SelectedField, FlowBloxFieldHelper.GetPropertyValueOrSelectedField(settings, typeof(EnumSettings).GetProperty(nameof(EnumSettings.Encoding))!));
        settings.Encoding_SelectedField = null;
        Assert.AreEqual(DotNetEncodingNames.ASCII, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Encoding));
    }

    [TestMethod]
    [DataRow("Tab", SpecialSeparator.Tab)]
    [DataRow("newline", SpecialSeparator.NewLine)]
    [DataRow("invalid", null)]
    [DataRow("", null)]
    [DataRow(null, null)]
    public void NullableEnum_ParsesOrUsesNull(string? value, SpecialSeparator? expected)
    {
        var settings = new EnumSettings
        {
            Separator_SelectedField = new FieldElement(FieldTypes.Text) { StringValue = value }
        };

        Assert.AreEqual(expected, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Separator));
        settings.Separator_SelectedField = null;
        Assert.AreEqual(SpecialSeparator.NewLine, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Separator));
        settings.Separator = null;
        Assert.IsNull(FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Separator));
    }

}
