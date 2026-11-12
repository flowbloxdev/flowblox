using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util.Fields;

namespace FlowBloxTest.FlowBlocks.IO;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class EnumFieldSelectionTests
{
    [TestMethod]
    [DataRow("UTF8", DotNetEncodingNames.UTF8)]
    [DataRow("utf16", DotNetEncodingNames.UTF16)]
    [DataRow("invalid", DotNetEncodingNames.Default)]
    [DataRow("", DotNetEncodingNames.Default)]
    [DataRow(null, DotNetEncodingNames.Default)]
    public void DynamicEnum_ParsesOrUsesDefault(string? value, DotNetEncodingNames expected)
    {
        var registry = new FlowBloxRegistry();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Encoding");
        field.StringValue = value;
        var settings = new EnumFieldSelectionSettings();
        settings.SetPropertySelectedField(nameof(settings.Encoding), field);

        Assert.AreEqual(expected, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Encoding));
        Assert.AreSame(field, FlowBloxFieldHelper.GetPropertyValueOrSelectedField(settings, typeof(EnumFieldSelectionSettings).GetProperty(nameof(settings.Encoding))!));
        settings.SetPropertySelectedField(nameof(settings.Encoding), null!);
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
        var registry = new FlowBloxRegistry();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Separator");
        field.StringValue = value;
        var settings = new EnumFieldSelectionSettings();
        settings.SetPropertySelectedField(nameof(settings.Separator), field);

        Assert.AreEqual(expected, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Separator));
        settings.SetPropertySelectedField(nameof(settings.Separator), null!);
        Assert.AreEqual(SpecialSeparator.NewLine, FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Separator));
        settings.Separator = null;
        Assert.IsNull(FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(settings, x => x.Separator));
    }

}
