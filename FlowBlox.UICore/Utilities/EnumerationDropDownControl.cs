using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FlowBlox.UICore.Utilities
{
    /// <summary>A value selector with a menu popup supporting nested enum groups.</summary>
    public sealed class EnumerationDropDownControl : ToggleButton
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(IReadOnlyList<EnumerationDropDown.Item>), typeof(EnumerationDropDownControl),
            new PropertyMetadata(null, OnValueChanged));

        public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
            nameof(SelectedValue), typeof(object), typeof(EnumerationDropDownControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };

        public EnumerationDropDownControl()
        {
            var content = new DockPanel();
            var arrow = new TextBlock { Text = "\u25be", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(arrow, Dock.Right);
            content.Children.Add(arrow);
            content.Children.Add(_label);
            Content = content;
            Unloaded += (_, _) => CloseDropDown();
            IsEnabledChanged += (_, _) => { if (!IsEnabled) CloseDropDown(); };
        }

        public IReadOnlyList<EnumerationDropDown.Item>? ItemsSource
        {
            get => (IReadOnlyList<EnumerationDropDown.Item>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public object? SelectedValue
        {
            get => GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        public ContextMenu? DropDownMenu { get; private set; }
        public event EventHandler? SelectionChanged;

        private static void OnValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var control = (EnumerationDropDownControl)target;
            control._label.Text = control.ItemsSource?.FirstOrDefault(item => Equals(item.Value, control.SelectedValue))?.Display ?? string.Empty;
            if (args.Property == SelectedValueProperty && !Equals(args.OldValue, args.NewValue))
                control.SelectionChanged?.Invoke(control, EventArgs.Empty);
        }

        protected override void OnChecked(RoutedEventArgs args)
        {
            base.OnChecked(args);
            if (!IsEnabled || !IsHitTestVisible || ItemsSource == null)
            {
                SetCurrentValue(IsCheckedProperty, false);
                return;
            }
            var menu = EnumerationDropDown.CreateMenu(this, ItemsSource);
            DropDownMenu = menu;
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(DropDownMenu, menu))
                    SetCurrentValue(IsCheckedProperty, false);
            };
            menu.IsOpen = true;
        }

        protected override void OnUnchecked(RoutedEventArgs args)
        {
            base.OnUnchecked(args);
            CloseDropDown();
        }

        private void CloseDropDown()
        {
            if (DropDownMenu != null)
                DropDownMenu.IsOpen = false;
            SetCurrentValue(IsCheckedProperty, false);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            var key = args.Key == Key.System ? args.SystemKey : args.Key;
            if (key == Key.F4 || (key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
            {
                SetCurrentValue(IsCheckedProperty, IsChecked != true);
                args.Handled = true;
                return;
            }
            if (key == Key.Escape)
            {
                CloseDropDown();
                args.Handled = true;
                return;
            }
            base.OnKeyDown(args);
        }
    }
}
