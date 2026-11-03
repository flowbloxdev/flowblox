using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.Fields;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Utilities;

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

    [STATestMethod]
    [DataRow(nameof(EnumSettings.Encoding))]
    [DataRow(nameof(EnumSettings.Separator))]
    public void EnumEditor_PreservesStaticSelectionAndSwitchesToSelectedField(string propertyName)
    {
        var settings = new EnumSettings();
        var property = typeof(EnumSettings).GetProperty(propertyName)!;
        var fieldProperty = typeof(EnumSettings).GetProperty(propertyName + "_SelectedField")!;
        var binding = new Binding(propertyName)
        {
            Source = settings,
            Mode = BindingMode.TwoWay
        };
        var editor = (Grid)new SimplePropertyInputControlWithOptionalButtonsCreator(null)
            .CreateSimplePropertyInputControlWithOptionalButtons(property, settings,
                new FlowBloxUIAttribute { UiOptions = UIOptions.EnableFieldSelection }, binding, false);
        var host = (Grid)editor.Children[0];
        var dropdown = (EnumerationDropDownControl)host.Children[0];
        Assert.AreEqual(property.GetValue(settings), dropdown.SelectedValue);
        Assert.AreEqual(Visibility.Visible, dropdown.Visibility);
        Assert.AreEqual(Visibility.Collapsed, host.Children[1].Visibility);

        dropdown.SelectedValue = propertyName == nameof(EnumSettings.Encoding)
            ? DotNetEncodingNames.UTF16
            : SpecialSeparator.Tab;
        Assert.AreEqual(dropdown.SelectedValue, property.GetValue(settings));

        fieldProperty.SetValue(settings, new FieldElement(FieldTypes.Text) { StringValue = "UTF8" });
        BindingOperations.GetBindingExpression(dropdown, UIElement.VisibilityProperty)!.UpdateTarget();
        BindingOperations.GetBindingExpression(host.Children[1], UIElement.VisibilityProperty)!.UpdateTarget();
        Assert.AreEqual(Visibility.Collapsed, dropdown.Visibility);
        Assert.AreEqual(Visibility.Visible, host.Children[1].Visibility);

        fieldProperty.SetValue(settings, null);
        BindingOperations.GetBindingExpression(dropdown, UIElement.VisibilityProperty)!.UpdateTarget();
        Assert.AreEqual(Visibility.Visible, dropdown.Visibility);
        Assert.AreEqual(property.GetValue(settings), dropdown.SelectedValue);
    }
}
