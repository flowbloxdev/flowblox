using FlowBlox.Core.Attributes;
using ICSharpCode.AvalonEdit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace FlowBlox.UICore.Factory.PropertyView;

internal static class MultilineEditorBehavior
{
    public static readonly DependencyProperty IsMultilineProperty = DependencyProperty.RegisterAttached(
        "IsMultiline",
        typeof(bool),
        typeof(MultilineEditorBehavior),
        new PropertyMetadata(false, OnIsMultilineChanged));

    public static bool GetIsMultiline(DependencyObject target) => (bool)target.GetValue(IsMultilineProperty);

    public static void SetIsMultiline(DependencyObject target, bool value) => target.SetValue(IsMultilineProperty, value);

    public static FrameworkElement Configure(FrameworkElement editor, object target, FlowBloxTextBoxAttribute attribute)
    {
        var container = ResizableControlContainer.Create(editor);
        var grip = container.Children.OfType<ResizeGrip>().Single();
        grip.SetBinding(UIElement.VisibilityProperty, new Binding
        {
            Source = editor,
            Path = new PropertyPath(IsMultilineProperty),
            Converter = new BooleanToVisibilityConverter()
        });

        if (string.IsNullOrWhiteSpace(attribute.IsMultilineMember))
        {
            SetIsMultiline(editor, attribute.MultiLine);
            Apply(editor, attribute.MultiLine);
        }
        else
        {
            var property = target.GetType().GetProperty(attribute.IsMultilineMember);
            if (property?.PropertyType != typeof(bool))
                throw new InvalidOperationException($"Multiline member '{attribute.IsMultilineMember}' must be a boolean property.");

            BindingOperations.SetBinding(editor, IsMultilineProperty, new Binding(attribute.IsMultilineMember)
            {
                Source = target,
                Mode = BindingMode.OneWay
            });
            Apply(editor, GetIsMultiline(editor));
        }

        editor.PreviewKeyDown += OnPreviewKeyDown;
        DataObject.AddPastingHandler(editor, OnPaste);
        ParentScrollViewerMouseWheelForwarder.Register(editor);
        return container;
    }

    private static void OnIsMultilineChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        Apply((FrameworkElement)target, (bool)args.NewValue);
    }

    private static void Apply(FrameworkElement editor, bool multiline)
    {
        editor.Height = multiline ? double.NaN : 24;
        if (editor is TextEditor code)
        {
            code.ShowLineNumbers = multiline;
            code.VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        }
        else if (editor is TextBox text)
        {
            text.AcceptsReturn = multiline;
            text.VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (!GetIsMultiline((DependencyObject)sender) && args.Key == Key.Enter)
            args.Handled = true;
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs args)
    {
        if (GetIsMultiline((DependencyObject)sender)
            || !args.DataObject.GetDataPresent(DataFormats.UnicodeText))
            return;

        var text = args.DataObject.GetData(DataFormats.UnicodeText) as string;
        if (text?.Contains('\r') == true || text?.Contains('\n') == true)
            args.DataObject = new DataObject(DataFormats.UnicodeText, text.ReplaceLineEndings(" "));
    }
}
