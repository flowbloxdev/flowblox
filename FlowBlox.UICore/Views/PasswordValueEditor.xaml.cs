using System.Windows;
using System.Windows.Controls;

namespace FlowBlox.UICore.Views;

/// <summary>
/// Revealing is local UI state and never changes the field's IsPassword flag.
/// </summary>
public partial class PasswordValueEditor : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PasswordValueEditor),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValueChanged));
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(PasswordValueEditor), new PropertyMetadata(false, ValueChanged));

    private bool _updating;

    public string Text
    {
        get => (string)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public PasswordValueEditor()
    {
        InitializeComponent();
        UpdateEditors();
    }

    private static void ValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PasswordValueEditor)sender).UpdateEditors();

    private void UpdateEditors()
    {
        if (MaskedEditor == null || RevealedEditor == null)
            return;
        _updating = true;
        if (MaskedEditor.Password != (Text ?? string.Empty))
            MaskedEditor.Password = Text ?? string.Empty;
        if (RevealedEditor.Text != (Text ?? string.Empty))
            RevealedEditor.Text = Text ?? string.Empty;
        MaskedEditor.IsEnabled = !IsReadOnly;
        RevealedEditor.IsReadOnly = IsReadOnly;
        _updating = false;
    }

    private void Password_Changed(object sender, RoutedEventArgs args)
    {
        if (!_updating && !IsReadOnly && Text != MaskedEditor.Password)
            SetCurrentValue(TextProperty, MaskedEditor.Password);
    }

    private void Text_Changed(object sender, TextChangedEventArgs args)
    {
        if (!_updating && !IsReadOnly && Text != RevealedEditor.Text)
            SetCurrentValue(TextProperty, RevealedEditor.Text);
    }

    private void Mask_Changed(object sender, RoutedEventArgs args)
    {
        if (MaskedEditor == null || RevealedEditor == null)
            return;
        var masked = MaskButton.IsChecked == true;
        MaskedEditor.Visibility = masked ? Visibility.Visible : Visibility.Collapsed;
        RevealedEditor.Visibility = masked ? Visibility.Collapsed : Visibility.Visible;
    }
}
