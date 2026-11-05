using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Automation;
using FlowBlox.Core.Util.Resources;
using SelectionDropDownControlResources = FlowBlox.UICore.Resources.ClassResources.SelectionDropDownControl;

namespace FlowBlox.UICore.Utilities
{
    /// <summary>
    /// A value selector with a shared menu popup, optional groups and selection marks.
    /// </summary>
    public class SelectionDropDownControl : ToggleButton
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(IReadOnlyList<SelectionDropDown.Item>), typeof(SelectionDropDownControl),
            new PropertyMetadata(null, OnValueChanged));

        public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
            nameof(SelectedValue), typeof(object), typeof(SelectionDropDownControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty IsSuggestionProperty = DependencyProperty.Register(
            nameof(IsSuggestion), typeof(bool), typeof(SelectionDropDownControl),
            new PropertyMetadata(false, OnValueChanged));

        private static readonly DependencyPropertyKey HasMatchingSuggestionPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasMatchingSuggestion), typeof(bool), typeof(SelectionDropDownControl), new PropertyMetadata(false));

        public static readonly DependencyProperty HasMatchingSuggestionProperty = HasMatchingSuggestionPropertyKey.DependencyProperty;

        private readonly TextBlock _label = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        private readonly Border _suggestionBadge;

        public SelectionDropDownControl()
        {
            _suggestionBadge = new Border
            {
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = FlowBloxResourceUtil.GetLocalizedString("SuggestionBadge", typeof(SelectionDropDownControlResources)),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            _suggestionBadge.SetResourceReference(StyleProperty, "FlowBlox.SuggestionBadge");
            var content = new DockPanel();
            var arrow = new TextBlock { Text = "\u25be", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(arrow, Dock.Right);
            content.Children.Add(arrow);
            DockPanel.SetDock(_suggestionBadge, Dock.Left);
            content.Children.Add(_suggestionBadge);
            content.Children.Add(_label);
            Content = content;
            Unloaded += (_, _) => CloseDropDown();
            IsEnabledChanged += (_, _) => { if (!IsEnabled) CloseDropDown(); };
        }

        public IReadOnlyList<SelectionDropDown.Item>? ItemsSource
        {
            get => (IReadOnlyList<SelectionDropDown.Item>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public object? SelectedValue
        {
            get => GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        public bool IsSuggestion
        {
            get => (bool)GetValue(IsSuggestionProperty);
            set => SetValue(IsSuggestionProperty, value);
        }

        public bool HasMatchingSuggestion => (bool)GetValue(HasMatchingSuggestionProperty);

        public ContextMenu? DropDownMenu { get; private set; }
        public event EventHandler? SelectionChanged;

        private static void OnValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var control = (SelectionDropDownControl)target;
            var selected = control.ItemsSource?.FirstOrDefault(item => Equals(item.Value, control.SelectedValue));
            var matchesSuggestion = control.IsSuggestion && selected?.Value != null;
            control.SetValue(HasMatchingSuggestionPropertyKey, matchesSuggestion);
            control._suggestionBadge.Visibility = control.IsSuggestion ? Visibility.Visible : Visibility.Collapsed;
            control._label.Text = control.IsSuggestion && !matchesSuggestion
                ? FlowBloxResourceUtil.GetLocalizedString("SelectSuggestion", typeof(SelectionDropDownControlResources))
                : selected?.Display ?? string.Empty;
            if (control.IsSuggestion)
            {
                control.ToolTip = FlowBloxResourceUtil.GetLocalizedString("SuggestionTooltip", typeof(SelectionDropDownControlResources));
                AutomationProperties.SetName(control, FlowBloxResourceUtil.GetLocalizedString("SelectSuggestion", typeof(SelectionDropDownControlResources)));
            }
            if (control.IsSuggestion && !matchesSuggestion)
                control._label.SetResourceReference(TextBlock.ForegroundProperty, "FlowBlox.Brush.MutedText");
            else
                control._label.ClearValue(TextBlock.ForegroundProperty);
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
            var menu = SelectionDropDown.CreateMenu(this, ItemsSource);
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
