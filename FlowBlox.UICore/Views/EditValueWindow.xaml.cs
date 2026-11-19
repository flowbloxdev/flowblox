using FlowBlox.UICore.Enums;
using MahApps.Metro.Controls;
using FlowBlox.UICore.Interfaces;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EditValueWindowResources = FlowBlox.UICore.Resources.EditValueWindow;

namespace FlowBlox.UICore.Views
{
    public partial class EditValueWindow : MetroWindow
    {
        private string _value = string.Empty;
        private int _selectionStart;
        private int _selectionLength;

        public EditValueWindow(string value, bool isRegex, bool isMultiline)
            : this(isRegex, isMultiline)
        {
            Title = EditValueWindowResources.TitleEditValue;
            ValueTextBox.Text = value ?? string.Empty;
            SuggestionsComboBox.Text = value ?? string.Empty;
        }

        public EditValueWindow(bool isRegex, bool isMultiline)
        {
            InitializeComponent();

            Title = EditValueWindowResources.TitleCreateValue;

            MaskRegexCheckBox.Visibility = isRegex ? Visibility.Visible : Visibility.Collapsed;
            MaskRegexCheckBox.IsChecked = isRegex;
            ApplyInputModeLayout(isMultiline);

            _selectionStart = 0;
            _selectionLength = 0;
        }

        private void ApplyInputModeLayout(bool isMultiline)
        {
            ValueTextBox.AcceptsReturn = isMultiline;
            ValueTextBox.TextWrapping = isMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ValueTextBox.VerticalScrollBarVisibility = isMultiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            ValueTextBox.HorizontalScrollBarVisibility = isMultiline ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            ValueTextBox.Height = isMultiline ? 180 : 32;
            SuggestionsComboBox.Height = isMultiline ? 36 : 32;
            Height = isMultiline ? 420 : 200;
        }

        public string GetValue() => _value;

        public bool IsMaskedRegexString() => MaskRegexCheckBox.IsChecked == true;

        public string Description
        {
            get => DescriptionTextBlock.Text;
            set => DescriptionTextBlock.Text = value;
        }

        public int SelectionStart
        {
            get => _selectionStart;
            set => _selectionStart = value;
        }

        public int SelectionLength
        {
            get => _selectionLength;
            set => _selectionLength = value;
        }

        public void SetSuggestions(List<string> suggestions, bool allowUserEdit)
        {
            ValueTextBox.Visibility = Visibility.Collapsed;
            SuggestionsComboBox.Visibility = Visibility.Visible;

            var previouslySet = SuggestionsComboBox.Text;

            SuggestionsComboBox.IsEditable = allowUserEdit;
            SuggestionsComboBox.ItemsSource = suggestions ?? Enumerable.Empty<string>();

            if (!string.IsNullOrWhiteSpace(previouslySet) && suggestions?.Contains(previouslySet) == true)
                SuggestionsComboBox.SelectedItem = previouslySet;
        }

        public void SetHeader(string header)
        {
            DescriptionTextBlock.Text = header;
        }

        public void SetParameterName(string parameterName)
        {
            ParameterValueTextBlock.Text = parameterName;
            ParameterLabel.Visibility = Visibility.Visible;
            ParameterValueTextBlock.Visibility = Visibility.Visible;
        }

        public void SetMode(EditMode mode)
        {
            if (mode == EditMode.Developer)
            {
                ValueTextBox.FontFamily = new System.Windows.Media.FontFamily("JetBrains Mono");
                SuggestionsComboBox.FontFamily = new System.Windows.Media.FontFamily("JetBrains Mono");
            }
            else
            {
                ValueTextBox.ClearValue(TextBox.FontFamilyProperty);
                SuggestionsComboBox.ClearValue(ComboBox.FontFamilyProperty);
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            FocusAndApplySelection();
        }

        private void FocusAndApplySelection()
        {
            if (ValueTextBox.Visibility == Visibility.Visible)
            {
                ValueTextBox.Focus();
                Keyboard.Focus(ValueTextBox);
                ValueTextBox.SelectionStart = Math.Max(0, Math.Min(_selectionStart, ValueTextBox.Text.Length));
                ValueTextBox.SelectionLength = Math.Max(0, Math.Min(_selectionLength, ValueTextBox.Text.Length - ValueTextBox.SelectionStart));
                return;
            }

            SuggestionsComboBox.Focus();
            Keyboard.Focus(SuggestionsComboBox);
            if (SuggestionsComboBox.Template.FindName("PART_EditableTextBox", SuggestionsComboBox) is TextBox editable)
            {
                editable.SelectionStart = Math.Max(0, Math.Min(_selectionStart, editable.Text.Length));
                editable.SelectionLength = Math.Max(0, Math.Min(_selectionLength, editable.Text.Length - editable.SelectionStart));
                editable.Focus();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var value = ValueTextBox.Visibility == Visibility.Visible
                ? ValueTextBox.Text
                : SuggestionsComboBox.Text;

            if (string.IsNullOrWhiteSpace(value))
            {
                FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>().ShowMessageBox(
                    FlowBloxResourceUtil.GetLocalizedString("ValidationErrorMessage", typeof(EditValueWindowResources)),
                    FlowBloxResourceUtil.GetLocalizedString("ValidationErrorTitle", typeof(EditValueWindowResources)),
                    FlowBloxMessageBoxTypes.Information);
                return;
            }

            _value = value;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ValueInput_Changed(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = true;
        }
    }
}
